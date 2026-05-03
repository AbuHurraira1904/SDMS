using SDMS.Domain.Models;

namespace SDMS.Domain.Scoring;

/// <summary>
/// Assigns an importance score (0–100) to each file in the analysis scope.
/// Pure computation — no filesystem access, no side effects.
/// </summary>
public interface IScoringEngine
{
    /// <summary>
    /// Scores all files in <see cref="AnalysisReport.ScopeFiles"/> and returns
    /// a corresponding list of ScoredFileNodes in the same order.
    /// </summary>
    /// <param name="report">The AnalysisReport produced by IAnalysisEngine.</param>
    /// <param name="weights">Scoring weights. Pass null to use ScoringWeights.Default.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<ScoredFileNode>> ScoreAsync(
        AnalysisReport report,
        ScoringWeights? weights = null,
        CancellationToken ct = default);
}
