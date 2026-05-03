using SDMS.Domain.Models;

namespace SDMS.Domain.Scanner;

/// <summary>
/// Traverses a filesystem path and builds a FileTree.
/// Implementations live in SDMS.Infrastructure.
/// </summary>
public interface IDirectoryScanner
{
    /// <summary>
    /// Scans the directory at <paramref name="rootPath"/> and returns a complete FileTree.
    /// </summary>
    /// <param name="rootPath">Absolute path to the root directory to scan.</param>
    /// <param name="options">Scan configuration (exclusions, depth limits, etc.).</param>
    /// <param name="progress">Optional progress reporter. Reports (filesScanned, currentPath).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<FileTree> ScanAsync(
        string rootPath,
        ScanOptions options,
        IProgress<(int FilesScanned, string CurrentPath)>? progress = null,
        CancellationToken ct = default);
}
