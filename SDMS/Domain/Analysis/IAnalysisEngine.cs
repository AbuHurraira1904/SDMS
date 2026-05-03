using SDMS.Domain.Models;

namespace SDMS.Domain.Analysis;

/// <summary>
/// Processes a FileTree into an AnalysisReport.
/// Pure computation — no filesystem access, no side effects.
/// </summary>
public interface IAnalysisEngine
{
    /// <summary>
    /// Analyzes the given <paramref name="tree"/> and returns a full AnalysisReport.
    /// </summary>
    /// <param name="tree">The FileTree produced by IDirectoryScanner.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AnalysisReport> AnalyzeAsync(
        FileTree tree,
        CancellationToken ct = default);
}
