using SDMS.Domain.Models;

namespace SDMS.Domain.Brain;

/// <summary>
/// Converts scored files and analysis data into a ProposedPlan.
/// The core decision-making module. Internal logic is replaceable (rule-based → ML).
/// </summary>
public interface IPlanGenerator
{
    /// <summary>
    /// Generates a ProposedPlan from the analysis report and scored files.
    /// </summary>
    /// <param name="report">The AnalysisReport from IAnalysisEngine.</param>
    /// <param name="scoredFiles">Scored files from IScoringEngine.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ProposedPlan> GenerateAsync(
        AnalysisReport report,
        List<ScoredFileNode> scoredFiles,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

