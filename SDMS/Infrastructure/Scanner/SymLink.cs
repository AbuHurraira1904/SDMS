using System.IO;

namespace SDMS.Infrastructure.Scanner;

/// <summary>
/// Prevents infinite traversal of circular symbolic links.
/// Tracks a composite key for every directory entered to detect loops.
/// </summary>
public sealed class SysLinkGuard : ISysLinkGuard_Interface
{
    // Composite key approximating (device_id, inode_id) without using native P/Invoke.
    private readonly record struct InodeKey(ulong DevHash, ulong InoHash);

    private readonly HashSet<InodeKey> _visited = [];
    private readonly object _lock = new();

    public bool ShouldFollow(string fullPath, bool followSymlinks, bool isSymlink)
    {
        // Respect policy: if symlinks are disabled, reject any reparse point immediately.
        if (isSymlink && !followSymlinks)
            return false;

        string pathToCheck = fullPath;

        if (isSymlink && followSymlinks)
        {
            try
            {
                // Resolve to the final target to ensure we detect loops pointing to the same physical disk location.
                pathToCheck = new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName 
                             ?? fullPath;
            }
            catch
            {
                return false; // Skip broken symlinks or paths with access issues.
            }
        }

        return TryRegister(pathToCheck);
    }

    public void RegisterRoot(string fullPath) => TryRegister(fullPath);

    private bool TryRegister(string path)
    {
        try
        {
            var key = BuildKey(path);
            lock (_lock)
            {
                // HashSet.Add returns false if the item exists, indicating a circular reference.
                return _visited.Add(key);
            }
        }
        catch
        {
            return false; // Refuse to follow if we cannot generate a unique key for the path.
        }
    }

    /// <summary>
    /// Generates a unique-ish key for a path. 
    /// Combines the root drive hash (Device proxy) with creation timestamps 
    /// and path hashes (Inode proxy) to distinguish between physical locations.
    /// </summary>
    private static InodeKey BuildKey(string path)
    {
        var fi = new FileInfo(path);
        
        ulong dev = (ulong)(Path.GetPathRoot(path)?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0);
        ulong ino = (ulong)fi.CreationTimeUtc.Ticks ^ (ulong)fi.FullName.GetHashCode(StringComparison.Ordinal);
        
        return new InodeKey(dev, ino);
    }
}