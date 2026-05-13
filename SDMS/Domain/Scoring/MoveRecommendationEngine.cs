using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SDMS.Domain.Models; 

namespace SDMS.Domain.Scoring;

// ── Data ──────────────────────────────────────────────────────────────────────

/// <summary>A real folder that exists on the drive.</summary>
public sealed record FolderInfo(string Name, string FullPath);

/// <summary>
/// Pre-computed lookup built ONCE from all candidates.
/// The service builds this once and reuses it for every file — O(1) per file
/// instead of O(folders) per file.
/// </summary>
public sealed class FolderIndex
{
    // category → folders that scored well for that category, sorted desc by affinity
    private readonly Dictionary<string, FolderInfo[]> _byCategory;
    // maximum candidates kept per category (keeps scoring fast on huge drives)
    private const int MaxPerCategory = 60;

    public FolderIndex(IReadOnlyList<FolderInfo> allFolders)
    {
        _byCategory = new Dictionary<string, FolderInfo[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in MoveRecommendationEngine.AllCategories)
        {
            _byCategory[category] = allFolders
                .Select(f => (folder: f, score: MoveRecommendationEngine.CategoryAffinityScore(f, category)))
                .Where(x => x.score > 0.05)
                .OrderByDescending(x => x.score)
                .Take(MaxPerCategory)
                .Select(x => x.folder)
                .ToArray();
        }
    }

    /// <summary>Returns the shortlisted candidates for a given category.</summary>
    public FolderInfo[] CandidatesFor(string category) =>
        _byCategory.TryGetValue(category, out var list) ? list : Array.Empty<FolderInfo>();
}

// ── Engine ────────────────────────────────────────────────────────────────────

public static class MoveRecommendationEngine
{
    // ── MimeClassifier output → engine category name ──────────────────────────
    // MimeClassifier.Classify() returns lowercase single words ("video", "image", etc.)
    // The engine uses display-friendly names ("Videos", "Pictures", etc.)
    // This bridge is the ONLY place that maps between the two — no duplicate ext list.

    private static readonly Dictionary<string, string> MimeToCategory =
        new(StringComparer.OrdinalIgnoreCase)
    {
        { "document",   "Documents" },
        { "image",      "Pictures"  },
        { "audio",      "Music"     },
        { "video",      "Videos"    },
        { "code",       "Code"      },
        { "archive",    "Archives"  },
        { "executable", "Software"  },
    };

    /// <summary>
    /// Resolves a file extension to the engine's category name by delegating
    /// to MimeClassifier — no duplicate extension dictionary needed here.
    /// Extension may be dot-prefixed (".mp4") or bare ("mp4"); both are handled.
    /// </summary>
    public static string GetCategory(string extension)
    {
        // MimeClassifier expects bare extension without dot (e.g. "mp4" not ".mp4")
        string bare = extension.TrimStart('.');
        string mime = MineClassifier.Classify(bare);          // → "video", "image", etc.
        return MimeToCategory.TryGetValue(mime, out var cat) ? cat : "Other";
    }

    public static readonly string[] AllCategories =
        { "Documents", "Music", "Videos", "Pictures", "Software", "Archives", "Code" };

    // ── Category → keywords ───────────────────────────────────────────────────

    private static readonly Dictionary<string, string[]> CategoryKeywords =
        new(StringComparer.OrdinalIgnoreCase)
    {
        { "Documents", new[] { "doc", "pdf", "note", "report", "invoice", "resume",
                                "letter", "contract", "work", "study", "school",
                                "uni", "college", "assignment", "lecture", "slide",
                                "book", "ebook", "manual", "guide", "form", "scan" } },
        { "Music",     new[] { "music", "mp3", "song", "audio", "track", "album",
                                "playlist", "juice", "sound", "beat", "vocal",
                                "record", "band", "artist", "mixtape" } },
        { "Videos",    new[] { "video", "movie", "film", "series", "show", "clip",
                                "episode", "season", "stream", "udemy", "course",
                                "tutorial", "lecture", "class", "lesson",
                                "javascript", "python", "programming", "dev",
                                "learning", "training", "bootcamp" } },
        { "Pictures",  new[] { "photo", "pic", "image", "img", "gallery", "dcim",
                                "shot", "camera", "wallpaper", "screenshot",
                                "portrait", "landscape", "pixel", "prodata",
                                "raw", "edited", "album" } },
        { "Software",  new[] { "setup", "install", "app", "bin", "driver",
                                "program", "tool", "utility", "portable",
                                "patch", "update", "release" } },
        { "Archives",  new[] { "archive", "backup", "pack", "bundle",
                                "compressed", "old", "storage", "vault" } },
        { "Code",      new[] { "code", "src", "source", "project", "repo",
                                "git", "dev", "script", "lib", "module",
                                "component", "api", "backend", "frontend",
                                "solution", "build", "test" } },
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Scores shortlisted candidates (from FolderIndex) for this file.
    /// Fast: only checks up to 60 pre-filtered folders per category.
    /// </summary>
    public static (string folderName, string folderFullPath, int probability) GetBestFolder(
        string      fileName,
        string      extension,
        string      currentFolderName,
        string      currentFullPath,
        FolderIndex index)
    {
        string category      = GetCategory(extension);   // delegates to MimeClassifier
        string fileNameNoExt = Path.GetFileNameWithoutExtension(fileName);

        // Only score the pre-filtered shortlist for this category
        FolderInfo[] shortlist = index.CandidatesFor(category);

        string bestName     = "";
        string bestFullPath = "";
        int    bestScore    = 0;

        foreach (var folder in shortlist)
        {
            // Skip: already in this folder
            if (folder.Name.Equals(currentFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip: folder is an ancestor of the file's current location
            if (!string.IsNullOrEmpty(currentFullPath) &&
                !string.IsNullOrEmpty(folder.FullPath) &&
                currentFullPath.StartsWith(
                    folder.FullPath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            int score = ScoreFolder(folder, fileName, fileNameNoExt, category);

            if (score > bestScore)
            {
                bestScore    = score;
                bestName     = folder.Name;
                bestFullPath = folder.FullPath;
            }
        }

        // Fallback: suggest a new generic folder when nothing real matched well
        if (bestScore < 40 && category != "Other")
            return (category, "", 58);

        return (bestName, bestFullPath, bestScore);
    }

    // ── Category affinity (used by FolderIndex to pre-filter) ────────────────

    /// <summary>
    /// How relevant is this folder to this category?
    /// Returns 0.0–1.0.  Called once per folder per category at index build time.
    /// </summary>
    public static double CategoryAffinityScore(FolderInfo folder, string category)
    {
        if (!CategoryKeywords.TryGetValue(category, out var keywords)) return 0.0;

        // Count keyword hits across the FULL PATH (all segments)
        string[] segments = folder.FullPath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                   StringSplitOptions.RemoveEmptyEntries);

        int hits = segments.Sum(seg =>
            keywords.Count(k => seg.Contains(k, StringComparison.OrdinalIgnoreCase)));

        return hits switch
        {
            0 => 0.0,
            1 => 0.50,
            2 => 0.75,
            3 => 0.90,
            _ => 0.98
        };
    }

    // ── Per-file scoring (runs on ≤60 candidates, not thousands) ─────────────

    private static int ScoreFolder(
        FolderInfo folder,
        string     fileName,
        string     fileNameNoExt,
        string     category)
    {
        double pathScore = PathCategoryScore(folder.FullPath, category);
        double nameScore = NameKeywordScore(folder.Name, category);
        double fileScore = FileRelevanceScore(fileNameNoExt, fileName, folder);

        // 40% path context, 35% folder name keywords, 25% filename relevance
        double raw = 0.40 * pathScore + 0.35 * nameScore + 0.25 * fileScore;
        return (int)Math.Round(raw * 100);
    }

    private static double PathCategoryScore(string fullPath, string category)
    {
        if (string.IsNullOrEmpty(fullPath) || category == "Other") return 0.05;
        if (!CategoryKeywords.TryGetValue(category, out var keywords)) return 0.05;

        string[] segments = fullPath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                   StringSplitOptions.RemoveEmptyEntries);

        int hits = segments.Sum(s =>
            keywords.Count(k => s.Contains(k, StringComparison.OrdinalIgnoreCase)));

        return hits switch { 0 => 0.05, 1 => 0.60, 2 => 0.80, 3 => 0.92, _ => 0.98 };
    }

    private static double NameKeywordScore(string folderName, string category)
    {
        if (string.IsNullOrEmpty(folderName) || category == "Other") return 0.05;
        if (!CategoryKeywords.TryGetValue(category, out var keywords)) return 0.05;

        int hits = keywords.Count(k =>
            folderName.Contains(k, StringComparison.OrdinalIgnoreCase));

        return hits switch { 0 => 0.05, 1 => 0.70, 2 => 0.88, _ => 0.97 };
    }

    private static double FileRelevanceScore(
        string fileNameNoExt, string fileName, FolderInfo folder)
    {
        if (fileName.Contains(folder.Name, StringComparison.OrdinalIgnoreCase))
            return 0.95;

        var tokens = fileNameNoExt
            .Split(new[] { ' ', '_', '-', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 4)
            .ToArray();

        int hits = tokens.Count(t =>
            folder.FullPath.Contains(t, StringComparison.OrdinalIgnoreCase));

        return hits switch { 0 => 0.15, 1 => 0.55, 2 => 0.75, _ => 0.88 };
    }
}