using SDMS.Domain.Scanner;
using SDMS.Domain.Models;

namespace SDMS.Infrastructure.Scanner;

/// <summary>
/// Logic for recursive filesystem traversal.
/// Maps OS entries into a hierarchical tree of FileNodes.
/// </summary>
public interface ITreeWalk_Interface
{
    /// <summary>
    /// Performs the recursive walk of the specified path.
    /// </summary>
    /// <param name="progress">
    /// Reports (FilesScanned, CurrentPath) to allow UI updates during long-running scans.
    /// </param>
    /// <param name="ct">Checked before entering each directory to allow graceful cancellation.</param>
    Task<FileNode> WalkAsync(
        string rootPath,
        ScanOptions options,
        IProgress<(int FilesScanned, string CurrentPath)>? progress = null,
        CancellationToken ct = default);

    // -- Statistics from the last completed scan --
    FolderAnalysisMetrics Analysis { get; }
    
}