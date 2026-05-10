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
    private long _bytes;
    private readonly List<string> _skipped = [];

    public int                  TotalFiles       => _files;
    public int                  TotalDirectories => _dirs;
    public long                 TotalSizeBytes   => _bytes;
    public int                  TotalIgnoredFiles => _ignored;
    public IReadOnlyList<string> SkippedPaths    => _skipped;

    public TreeWalker(
        IMetaDataExtractor_Interface meta,
        ISysLinkGuard_Interface      guard,
        IMountDectector_Interface    mounts)
    {
        _meta   = meta;
        _guard  = guard;
        _mounts = mounts;
    }

    /// <inheritdoc/>
    public async Task<FileNode> WalkAsync(
        string                                              rootPath,
        ScanOptions                                         options,
        IProgress<(int FilesScanned, string CurrentPath)>? progress = null,
        CancellationToken                                   ct       = default)
    {
        // Reset counters for this scan run.
        _files = _dirs = _ignored = 0;
        _bytes = 0;
        _skipped.Clear();

        rootPath = Path.GetFullPath(rootPath);
        _guard.RegisterRoot(rootPath);

        // Offload the CPU-bound walk to a thread-pool thread so the calling
        // async context (e.g. a UI thread) is not blocked.
        return await Task.Run(
            () => WalkDirectory(new DirectoryInfo(rootPath), options, progress, ct, depth: 0),
            ct);
    }

    // ── Core recursion ────────────────────────────────────────────────────────

    private FileNode WalkDirectory(
        DirectoryInfo                                       di,
        ScanOptions                                         options,
        IProgress<(int FilesScanned, string CurrentPath)>? progress,
        CancellationToken                                   ct,
        int                                                 depth)
    {
        ct.ThrowIfCancellationRequested();

        _dirs++;
        var meta = _meta.Extract(di);

        var dirNode = new FileNode
        {
            Name        = meta.Name,
            FullPath    = meta.FullPath,
            IsDirectory = true,
            SizeBytes   = 0,
            CreatedAt   = meta.CreatedAt ?? DateTime.MinValue,
            ModifiedAt  = meta.ModifiedAt,
            AccessedAt  = meta.AccessedAt,
            Attributes  = meta.Attributes,
            SymlinkTarget = meta.SymlinkTarget,
            MimeType    = "directory",
            Children    = [],
        };

        // Stop recursing when depth limit is reached — return node without children.
        if (options.MaxDepth.HasValue && depth >= options.MaxDepth.Value)
            return dirNode;

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = di.EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                IgnoreInaccessible    = true,
                RecurseSubdirectories = false,
                AttributesToSkip      = 0,
            });
        }
        catch (UnauthorizedAccessException)
        {
            _skipped.Add(di.FullName);
            return dirNode;
        }
        catch (IOException)
        {
            _skipped.Add(di.FullName);
            return dirNode;
        }

        // Sort: directories first, then files, each group alphabetically.
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
                // Apply exclusion rules.
                if (options.ExcludedDirectoryNames.Contains(
                        subDir.Name, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (!options.IncludeHidden &&
                    (subDir.Attributes & FileAttributes.Hidden) != 0)
                    continue;

                if (!options.IncludeSystemFiles &&
                    (subDir.Attributes & FileAttributes.System) != 0)
                    continue;

                // Symlink guard — prevents infinite loops.
                if (!_guard.ShouldFollow(subDir.FullName, options.FollowSymlinks, entryIsSymlink))
                    continue;

                var child = WalkDirectory(subDir, options, progress, ct, depth + 1);
                dirNode.Children.Add(child);
            }
            else if (entry is FileInfo fi)
            {
                // Apply per-file exclusion rules.
                var fileMeta = _meta.Extract(fi);

                if (!options.IncludeHidden &&
                    (fileMeta.Attributes & FileAttributes.Hidden) != 0)
                { _ignored++; continue; }

                if (!options.IncludeSystemFiles &&
                    (fileMeta.Attributes & FileAttributes.System) != 0)
                { _ignored++; continue; }

                string ext = Path.GetExtension(fi.Name).ToLowerInvariant();
                
                if (options.ExcludedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                { _ignored++; continue; }

                if (options.MaxFileSizeBytes.HasValue &&
                    fileMeta.SizeBytes > options.MaxFileSizeBytes.Value)
                { _ignored++; continue; }

                if (fileMeta.PermissionError)
                { _skipped.Add(fi.FullName); continue; }

                string mimeType = MineClassifier.Classify(
                    Path.GetExtension(fi.Name).TrimStart('.').ToLowerInvariant());

                var fileNode = new FileNode
                {
                    Name          = fileMeta.Name,
                    FullPath      = fileMeta.FullPath,
                    IsDirectory   = false,
                    SizeBytes     = fileMeta.SizeBytes,
                    CreatedAt     = fileMeta.CreatedAt ?? DateTime.MinValue,
                    ModifiedAt    = fileMeta.ModifiedAt,
                    AccessedAt    = fileMeta.AccessedAt,
                    Attributes    = fileMeta.Attributes,
                    SymlinkTarget = fileMeta.SymlinkTarget,
                    MimeType      = mimeType,
                };

                dirNode.Children.Add(fileNode);
                _files++;
                _bytes += fileNode.SizeBytes;

                // Report progress after every file.
                progress?.Report((_files, fi.FullName));
            }
        }

        return dirNode;
    }
}
