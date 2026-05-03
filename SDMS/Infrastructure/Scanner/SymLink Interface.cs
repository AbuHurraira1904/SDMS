namespace SDMS.Infrastructure.Scanner;

/// <summary>
/// Guards against infinite recursion caused by circular symbolic links.
/// A single instance must be shared across the entire recursive walk.
/// </summary>
public interface ISysLinkGuard_Interface
{
    /// <summary>
    /// Checks if it is safe to enter a directory. 
    /// Detects loops and respects the 'FollowSymlinks' policy.
    /// </summary>
    /// <param name="fullPath">Absolute path to verify.</param>
    /// <param name="followSymlinks">Whether the scanner is allowed to follow links.</param>
    /// <param name="isSymlink">Pre-detected symlink status from the OS metadata.</param>
    /// <returns>True if safe to proceed; False if it would cause a loop or violates policy.</returns>
    bool ShouldFollow(string fullPath, bool followSymlinks, bool isSymlink);

    /// <summary>
    /// Registers the starting root directory. 
    /// Essential to prevent a symlink deep in the tree from pointing back to the start.
    /// </summary>
    void RegisterRoot(string fullPath);
}