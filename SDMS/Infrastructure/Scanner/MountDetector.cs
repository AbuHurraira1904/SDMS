using System.IO;

namespace SDMS.Infrastructure.Scanner;

/// <summary>
/// Detects mount points and network drive roots.
/// Uses Lazy initialization to cache drive information, avoiding expensive OS calls during the walk.
/// </summary>
public sealed class MountDetector : IMountDectector_Interface
{
    private readonly Lazy<IReadOnlySet<string>> _networkRoots = new(CollectNetworkRoots);
    private readonly Lazy<IReadOnlySet<string>> _allRoots = new(CollectAllRoots);

    public bool IsMount(string fullPath)
    {
        string path = Path.GetFullPath(fullPath);
        return OperatingSystem.IsWindows() ? IsMountWindows(path) : IsMountPosix(path);
    }

    public bool IsNetworkDrive(string fullPath)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(fullPath)) ?? string.Empty;
        return _networkRoots.Value.Contains(root);
    }

    // -- POSIX Implementation --

    private bool IsMountPosix(string path)
    {
        if (_allRoots.Value.Contains(path)) return true;

        // Linux-specific fallback: check /proc/mounts for bind-mounts or overlayfs
        if (OperatingSystem.IsLinux())
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/mounts"))
                {
                    var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (cols.Length >= 2 && string.Equals(cols[1], path, StringComparison.Ordinal))
                        return true;
                }
            }
            catch { /* Handled: /proc/mounts may be restricted in some containers */ }
        }
        return false;
    }

    // -- Windows Implementation --

    private static bool IsMountWindows(string path)
    {
        // Check for drive root pattern (e.g., "C:\")
        if (path.Length == 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\')
            return true;

        // Check for UNC root pattern (\\server\share)
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 2) return true;
        }
        return false;
    }

    // -- Collection Helpers --

    private static IReadOnlySet<string> CollectNetworkRoots() => CollectDrives(DriveType.Network);

    private static IReadOnlySet<string> CollectAllRoots() => CollectDrives(null);

    private static IReadOnlySet<string> CollectDrives(DriveType? type)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (type == null || d.DriveType == type)
                    set.Add(d.RootDirectory.FullName);
            }
        }
        catch { /* DriveInfo enumeration can fail in restricted environments */ }
        return set;
    }
}