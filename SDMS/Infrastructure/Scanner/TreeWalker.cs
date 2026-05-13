// ============================================================
// TreeWalker.cs  →  SDMS.Infrastructure/Scanner/
// Concrete implementation of ITreeWalk_Interface.
// Uses async-friendly Task.Run + lazy EnumerateFileSystemInfos.
// Flags inaccessible nodes rather than crashing the scan.
// ============================================================

using System.IO;
using SDMS.Domain.Scanner;
using SDMS.Domain.Models;

namespace SDMS.Infrastructure.Scanner;

/// <summary>
/// Recursively walks the filesystem and builds a <see cref="FileNode"/> tree.
/// Receives all OS-touching dependencies via constructor injection:
/// <see cref="IMetaDataExtractor_Interface"/>, <see cref="ISysLinkGuard_Interface"/>,
/// <see cref="IMountDectector_Interface"/>.
/// </summary>
public sealed class TreeWalker : ITreeWalk_Interface
{
    private readonly IMetaDataExtractor_Interface _meta;
    private readonly ISysLinkGuard_Interface      _guard;
    private readonly IMountDectector_Interface    _mounts;

    // Counters reset at the start of each WalkAsync call.
    private int  _files, _dirs, _ignored;
    private FolderAnalysisMetrics? _metrics;
    private long _bytes;
    private readonly List<string> _skipped = [];
    private string RootPath;
    
    public FolderAnalysisMetrics Analysis => _metrics;

    public TreeWalker(
        IMetaDataExtractor_Interface meta,
        ISysLinkGuard_Interface      guard,
        IMountDectector_Interface    mounts)
    {
        _meta   = meta;
        _guard  = guard;
        _mounts = mounts;
    }
    
    // helper for updating Analysis Metrics
    private void UpdateDashboardMetrics(ExtractedMetaData meta)
    {
        string ext = Path.GetExtension(meta.Name).ToLowerInvariant().TrimStart('.');
        if (string.IsNullOrEmpty(ext)) ext = "no-extension";

        // 1. Tally Extensions
        _metrics.ExtensionCounts[ext] = _metrics.ExtensionCounts.GetValueOrDefault(ext) + 1;
        _metrics.ExtensionSizes[ext] = _metrics.ExtensionSizes.GetValueOrDefault(ext) + meta.SizeBytes;

        // 2. Tally Categories (using your MimeClassifier logic)
        string category = Mimeclassifier.Classify(ext); // e.g., "Image", "Video"
        _metrics.CategoryCounts[category] = _metrics.CategoryCounts.GetValueOrDefault(category) + 1;

        // 3. Flagged Files
        if ((meta.Attributes & FileAttributes.Hidden) != 0) _metrics.HiddenPaths.Add(meta.FullPath);
        if ((meta.Attributes & FileAttributes.System) != 0) _metrics.SystemPaths.Add(meta.FullPath);

        // 4. Timestamps
        if (meta.ModifiedAt < _metrics.OldestFile && meta.ModifiedAt > DateTime.MinValue) 
            _metrics.OldestFile = meta.ModifiedAt;
    
        if (meta.ModifiedAt > _metrics.NewestFile) 
            _metrics.NewestFile = meta.ModifiedAt;
    }
    
    
    public async Task<FileNode> WalkAsync(
        string rootPath,
        ScanOptions options,
        IProgress<(int FilesScanned, string CurrentPath)>? progress = null,
        CancellationToken ct = default)
    {
        _metrics = new FolderAnalysisMetrics();
        _files = _dirs = _ignored = 0;
        _bytes = 0;
        _skipped.Clear();

        rootPath = Path.GetFullPath(rootPath);
        RootPath = rootPath;
        _guard.RegisterRoot(rootPath);

        var root = await Task.Run(
            () => WalkDirectory(new DirectoryInfo(rootPath), options, progress, ct, depth: 0), ct);

        // Sync counters into metrics after walk completes
        _metrics.TotalFiles       = _files;
        _metrics.TotalDirectories = _dirs;
        _metrics.TotalSizeBytes   = _bytes;
        _metrics.TotalIgnoredFiles = _ignored;
        _metrics.SkippedPaths.AddRange(_skipped);

        return root;
    }

    // ── Core recursion ────────────────────────────────────────────────────────

    private FileNode WalkDirectory(DirectoryInfo di,
        ScanOptions options,
        IProgress<(int FilesScanned, string CurrentPath)>? progress,
        CancellationToken ct,
        int depth)
    {
        ct.ThrowIfCancellationRequested();

        _dirs++;
        int childDirs = 0;
        int childFiles = 0;
        
        var meta = _meta.Extract(di);
        
        // We start this folder's "Raw Physical Size" at 0.
        long folderPhysicalSize = 0;

        var dirNode = BuildDir(meta, depth);
        
        // depth check
        if (options.MaxDepth.HasValue && depth >= options.MaxDepth.Value)
        {
            dirNode.SizeBytes = GetQuickFolderSize(di); 
            return dirNode;
        }
        
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = di.EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
            });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _skipped.Add(di.FullName);
            return dirNode;
        }

        var sorted = entries
            .OrderBy(e => e is FileInfo ? 1 : 0)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var entry in sorted)
        {
            ct.ThrowIfCancellationRequested();
            bool entryIsSymlink = (entry.Attributes & FileAttributes.ReparsePoint) != 0;

            if (entry is DirectoryInfo subDir)
            {
                // updating children data
                    // ie the files and folders directly in it
                childDirs++;
                
                
                // CHECK EXCLUSION FIRST — skip the whole subtree if excluded by name/hidden/system
                // BUT still recurse for size accuracy if you want Windows-accurate folder sizes
                bool excluded = IsDirectoryExcluded(subDir, options, entryIsSymlink);

                if (!excluded)
                {
                    var childDirNode = WalkDirectory(subDir, options, progress, ct, depth + 1);
                    folderPhysicalSize += childDirNode.SizeBytes;

                    if (depth == 0)
                        _metrics!.ImmediateSubDirs.Add(
                            new SubDirSummary(subDir.Name, subDir.FullName, childDirNode.SizeBytes));

                    dirNode.Children.Add(childDirNode);
                }
                else
                {
                    // Still count size even for excluded dirs so parent sizes stay accurate
                    // This matches what Windows Explorer shows
                    folderPhysicalSize += GetQuickFolderSize(subDir);
                }
            }
            else if (entry is FileInfo fi)
            {
                // Always accumulate size — excluded or not — for Windows-accurate totals
                folderPhysicalSize += fi.Length;
                childFiles++;
                
                
                var fileMeta = _meta.Extract(fi);

                // Metrics for ALL files (hidden, system included) — gives you the real picture
                // If you only want metrics for visible files, move this inside the if below
                UpdateDashboardMetrics(fileMeta);

                if (!IsFileIncluded(fi, options, fileMeta))
                {
                    _ignored++;
                    continue;
                }

                var fileNode = BuildFile(fileMeta);
                fileNode.Depth = depth;
                dirNode.Children.Add(fileNode);
                _files++;
                _bytes += fileNode.SizeBytes;
                progress?.Report((_files, fi.FullName));
            }
        }

        // adjusting the size of folder
        dirNode.SizeBytes = folderPhysicalSize;
        dirNode.numChildDirs = childDirs;
        dirNode.numChildFiles = childFiles;
        
        foreach (FileNode child in dirNode.Children)
        {
            child.NumSiblings = dirNode.numChildFiles;
        }
        
        
        
        return dirNode;
    }
    
    
    // Helper

    private static long GetQuickFolderSize(DirectoryInfo di)
    {
        try
        {
            // EnumerateFiles with SearchOption.AllDirectories is the closest 
            // standard .NET way to simulate the "Properties" dialog sum.
            return di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }
        catch
        {
            // If we can't even get a quick sum (Permission denied), we report 0
            return 0;
        }
    }
    
    private FileNode BuildDir(ExtractedMetaData meta, int d)
    {
        string relPath = Path.GetRelativePath(RootPath, meta.FullPath);
        string[] parents = relPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        return new FileNode
        {
            Name = meta.Name,
            FullPath = meta.FullPath,
            RelativePath =  relPath,
            ParentChain = parents[0..^1],
            Depth = d,
            IsDirectory = true,
            SizeBytes = 0,
            numChildDirs = 0,
            numChildFiles = 0,
            CreatedAt = meta.CreatedAt ?? DateTime.MinValue,
            ModifiedAt = meta.ModifiedAt,
            AccessedAt = meta.AccessedAt,
            Attributes = meta.Attributes,
            SymlinkTarget = meta.SymlinkTarget,
            MimeType = "directory",
            Children = [],
        };
    }

    private FileNode BuildFile(ExtractedMetaData fileMeta)
    {
        string mimeType = Mimeclassifier.Classify(
            Path.GetExtension(fileMeta.Name).TrimStart('.').ToLowerInvariant());
        
        string relPath = Path.GetRelativePath(RootPath, fileMeta.FullPath);
        string[] parents = relPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        
        return new FileNode
        {
            Name = fileMeta.Name,
            FullPath = fileMeta.FullPath,
            IsDirectory = false,
            SizeBytes = fileMeta.SizeBytes,
            CreatedAt = fileMeta.CreatedAt ?? DateTime.MinValue,
            ModifiedAt = fileMeta.ModifiedAt,
            AccessedAt = fileMeta.AccessedAt,
            Attributes = fileMeta.Attributes,
            SymlinkTarget = fileMeta.SymlinkTarget,
            MimeType = mimeType,
            RelativePath =  relPath,
            ParentChain = parents[0..^1],
            Depth = 0,
        };
    }
    
    
    private bool IsDirectoryExcluded(DirectoryInfo di, ScanOptions opt, bool isSymlink)
    {
        if (opt.ExcludedDirectoryNames.Contains(di.Name, StringComparer.OrdinalIgnoreCase)) return true;
        if (!opt.IncludeHidden && (di.Attributes & FileAttributes.Hidden) != 0) return true;
        if (!opt.IncludeSystemFiles && (di.Attributes & FileAttributes.System) != 0) return true;
        return !_guard.ShouldFollow(di.FullName, opt.FollowSymlinks, isSymlink);
    }

    private bool IsFileIncluded(FileInfo fi, ScanOptions opt, ExtractedMetaData meta)
    {
        if (meta.PermissionError) return false;
        if (!opt.IncludeHidden && (meta.Attributes & FileAttributes.Hidden) != 0) return false;
        if (!opt.IncludeSystemFiles && (meta.Attributes & FileAttributes.System) != 0) return false;
    
        string ext = Path.GetExtension(fi.Name).ToLowerInvariant();
        if (opt.ExcludedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return false;
    
        return true;
    }
}
