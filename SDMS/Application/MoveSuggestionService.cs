using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SDMS.Domain.Models;
using SDMS.Domain.Scoring;

namespace SDMS.Application;

public static class MoveSuggestionService
{
    // =========================================================================
    // PRIMARY ENTRY POINT — uses the FileTree already in memory
    // No disk walk, no JSON re-parse. O(1) access to the tree.
    // =========================================================================

    /// <summary>
    /// Analyze directly from the in-memory <see cref="FileTree"/> produced by
    /// Module 1.  This is the fast path — the tree is already built, so we
    /// just walk the <see cref="FileNode"/> graph without touching disk again.
    /// </summary>
    public static List<MoveRecommendation> AnalyzeFromTree(
        FileTree tree,
        int?     maxDepth = null)
    {
        var results = new List<MoveRecommendation>();

        if (tree?.Root is null)
        {
            Console.Error.WriteLine("[Module 2] FileTree has no root node.");
            return results;
        }

        // ── Pass 1: collect all FolderInfo from the in-memory tree ────────────
        var allFolders = new List<FolderInfo>();
        CollectFolderInfoFromNode(tree.Root, currentDepth: 0, maxDepth, allFolders);

        Console.Error.WriteLine(
            $"[Module 2] {allFolders.Count} real folders collected from tree — building index…");

        // ── Build index once (pre-groups folders by category, max 60 each) ────
        var index = new FolderIndex(allFolders);

        Console.Error.WriteLine("[Module 2] Index ready. Scoring files…");

        // ── Pass 2: walk file nodes and score each one ─────────────────────────
        WalkNode(tree.Root, currentDepth: 0, maxDepth, index, results);

        return results;
    }

    // =========================================================================
    // FALLBACK ENTRY POINT — reads from JSON if tree is unavailable
    // =========================================================================

    public static List<MoveRecommendation> AnalyzeFromJson(
        string jsonPath,
        string rootPath,
        int?   maxDepth = null)
    {
        if (File.Exists(jsonPath))
        {
            try   { return AnalyzeJson(jsonPath, maxDepth); }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[Module 2] Warning: could not parse '{jsonPath}': {ex.Message}");
                Console.Error.WriteLine("[Module 2] Falling back to live disk scan.");
            }
        }
        else
        {
            Console.Error.WriteLine(
                $"[Module 2] '{jsonPath}' not found — falling back to live disk scan.");
        }

        return AnalyzeFromDisk(rootPath, maxDepth);
    }

    // =========================================================================
    // IN-MEMORY TREE WALK  (FileNode graph)
    // =========================================================================

    /// <summary>
    /// Recursively collect every directory node as a <see cref="FolderInfo"/>.
    /// </summary>
    private static void CollectFolderInfoFromNode(
        FileNode        node,
        int             currentDepth,
        int?            maxDepth,
        List<FolderInfo> folders)
    {
        // Every node we visit is a directory (we only recurse into directories)
        if (!string.IsNullOrEmpty(node.Name))
            folders.Add(new FolderInfo(node.Name, node.FullPath ?? node.Name));

        // Stop descending if we've hit the depth limit
        if (maxDepth.HasValue && currentDepth >= maxDepth.Value) return;

        if (node.Children is null) return;

        foreach (var child in node.Children)
            if (child.IsDirectory)
                CollectFolderInfoFromNode(child, currentDepth + 1, maxDepth, folders);
    }

    /// <summary>
    /// Walk the node tree, score every file, add a recommendation for each.
    /// </summary>
    private static void WalkNode(
        FileNode                 node,
        int                      currentDepth,
        int?                     maxDepth,
        FolderIndex              index,
        List<MoveRecommendation> results)
    {
        if (maxDepth.HasValue && currentDepth > maxDepth.Value) return;
        if (node.Children is null) return;

        string folderName     = node.Name     ?? "";
        string folderFullPath = node.FullPath ?? folderName;

        foreach (var child in node.Children)
        {
            if (child.IsDirectory)
            {
                // Recurse into subdirectory
                WalkNode(child, currentDepth + 1, maxDepth, index, results);
            }
            else
            {
                // Score this file
                string fileName = child.Name ?? "";
                if (string.IsNullOrEmpty(fileName)) continue;

                string filePath = child.FullPath ?? Path.Combine(folderFullPath, fileName);

                // FileNode has an Extension property — use it directly.
                // It may be stored without the dot ("mp4") or with (".mp4").
                string rawExt = child.Extension ?? Path.GetExtension(fileName).TrimStart('.');
                string ext    = string.IsNullOrEmpty(rawExt)
                    ? ""
                    : rawExt.StartsWith('.') ? rawExt : "." + rawExt;

                var (targetName, targetFullPath, probability) =
                    MoveRecommendationEngine.GetBestFolder(
                        fileName, ext, folderName, filePath, index);

                results.Add(new MoveRecommendation
                {
                    FileName            = fileName,
                    FullPath            = filePath,
                    CurrentFolder       = folderName,
                    RecommendedFolder   = targetName,
                    RecommendedFullPath = targetFullPath,
                    Probability         = probability,
                    Depth               = currentDepth
                });
            }
        }
    }

    // =========================================================================
    // JSON FALLBACK PATH  (reads filetree.json — used only if tree unavailable)
    // =========================================================================

    private static List<MoveRecommendation> AnalyzeJson(string jsonPath, int? maxDepth)
    {
        using var stream = File.OpenRead(jsonPath);
        using var doc    = JsonDocument.Parse(stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling     = JsonCommentHandling.Skip
            });

        var results = new List<MoveRecommendation>();
        var root    = doc.RootElement;

        JsonElement? rootNode = FindRootNode(root);
        if (rootNode is null)
        {
            Console.Error.WriteLine("[Module 2] Could not locate root directory node in JSON.");
            return results;
        }

        var allFolders = new List<FolderInfo>();
        CollectFolderInfoFromJson(rootNode.Value, 0, maxDepth, allFolders);

        Console.Error.WriteLine(
            $"[Module 2] {allFolders.Count} real folders collected from JSON — building index…");

        var index = new FolderIndex(allFolders);

        Console.Error.WriteLine("[Module 2] Index ready. Scoring files…");

        WalkJsonNode(rootNode.Value, 0, maxDepth, index, results);
        return results;
    }

    private static JsonElement? FindRootNode(JsonElement doc)
    {
        if (doc.TryGetProperty("root", out var rootProp) &&
            rootProp.ValueKind == JsonValueKind.Object)
            return rootProp;

        if (doc.ValueKind == JsonValueKind.Object &&
            (doc.TryGetProperty("children", out _) || doc.TryGetProperty("name", out _)))
            return doc;

        return null;
    }

    private static void CollectFolderInfoFromJson(
        JsonElement     node,
        int             currentDepth,
        int?            maxDepth,
        List<FolderInfo> folders)
    {
        string name     = GetString(node, "name")     ?? "";
        string fullPath = GetString(node, "fullPath") ?? "";
        if (!string.IsNullOrEmpty(name))
            folders.Add(new FolderInfo(name, fullPath));

        if (maxDepth.HasValue && currentDepth >= maxDepth.Value) return;

        foreach (var child in ChildDirectories(node))
            CollectFolderInfoFromJson(child, currentDepth + 1, maxDepth, folders);
    }

    private static void WalkJsonNode(
        JsonElement              node,
        int                      currentDepth,
        int?                     maxDepth,
        FolderIndex              index,
        List<MoveRecommendation> results)
    {
        if (maxDepth.HasValue && currentDepth > maxDepth.Value) return;

        string folderName     = GetString(node, "name")     ?? "";
        string folderFullPath = GetString(node, "fullPath") ?? "";

        foreach (var fileNode in ChildFiles(node))
        {
            string fileName = GetString(fileNode, "name") ?? "";
            if (string.IsNullOrEmpty(fileName)) continue;

            string filePath = GetString(fileNode, "fullPath")
                              ?? Path.Combine(folderFullPath, fileName);

            string rawExt = GetString(fileNode, "extension")
                            ?? Path.GetExtension(fileName).TrimStart('.');
            string ext    = string.IsNullOrEmpty(rawExt) ? "" : "." + rawExt;

            var (targetName, targetFullPath, probability) =
                MoveRecommendationEngine.GetBestFolder(
                    fileName, ext, folderName, filePath, index);

            results.Add(new MoveRecommendation
            {
                FileName            = fileName,
                FullPath            = filePath,
                CurrentFolder       = folderName,
                RecommendedFolder   = targetName,
                RecommendedFullPath = targetFullPath,
                Probability         = probability,
                Depth               = currentDepth
            });
        }

        foreach (var child in ChildDirectories(node))
            WalkJsonNode(child, currentDepth + 1, maxDepth, index, results);
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────

    private static IEnumerable<JsonElement> ChildFiles(JsonElement node)
    {
        if (!node.TryGetProperty("children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var child in children.EnumerateArray())
            if (child.TryGetProperty("isDirectory", out var d) &&
                d.ValueKind == JsonValueKind.False)
                yield return child;
    }

    private static IEnumerable<JsonElement> ChildDirectories(JsonElement node)
    {
        if (!node.TryGetProperty("children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var child in children.EnumerateArray())
            if (child.TryGetProperty("isDirectory", out var d) &&
                d.ValueKind == JsonValueKind.True)
                yield return child;
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() : null;

    // =========================================================================
    // DISK FALLBACK  (last resort when both tree and JSON are unavailable)
    // =========================================================================

    public static List<MoveRecommendation> AnalyzeFromDisk(
        string rootPath, int? maxDepth = null)
    {
        var results = new List<MoveRecommendation>();
        if (!Directory.Exists(rootPath)) return results;

        var allFolders = new List<FolderInfo>();
        CollectFolderInfoDisk(rootPath, 0, maxDepth, allFolders);
        var index = new FolderIndex(allFolders);
        WalkDirectoryDisk(rootPath, 0, maxDepth, index, results);
        return results;
    }

    private static void CollectFolderInfoDisk(
        string path, int depth, int? maxDepth, List<FolderInfo> folders)
    {
        if (maxDepth.HasValue && depth >= maxDepth.Value) return;
        string[] subDirs;
        try { subDirs = Directory.GetDirectories(path); }
        catch { return; }

        foreach (var dir in subDirs)
        {
            string? name = Path.GetFileName(dir);
            if (name is not null) folders.Add(new FolderInfo(name, dir));
            CollectFolderInfoDisk(dir, depth + 1, maxDepth, folders);
        }
    }

    private static void WalkDirectoryDisk(
        string path, int depth, int? maxDepth,
        FolderIndex index, List<MoveRecommendation> results)
    {
        if (maxDepth.HasValue && depth > maxDepth.Value) return;

        string folderName = Path.GetFileName(path) ?? path;
        string[] files;
        try { files = Directory.GetFiles(path); }
        catch { files = Array.Empty<string>(); }

        foreach (var file in files)
        {
            string fileName = Path.GetFileName(file);
            string ext      = Path.GetExtension(file);

            var (targetName, targetFullPath, probability) =
                MoveRecommendationEngine.GetBestFolder(
                    fileName, ext, folderName, file, index);

            results.Add(new MoveRecommendation
            {
                FileName            = fileName,
                FullPath            = file,
                CurrentFolder       = folderName,
                RecommendedFolder   = targetName,
                RecommendedFullPath = targetFullPath,
                Probability         = probability,
                Depth               = depth
            });
        }

        string[] subDirs;
        try { subDirs = Directory.GetDirectories(path); }
        catch { return; }

        foreach (var dir in subDirs)
            WalkDirectoryDisk(dir, depth + 1, maxDepth, index, results);
    }
}