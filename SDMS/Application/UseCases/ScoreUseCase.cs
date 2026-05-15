// ============================================================
// ScoreUseCase.cs  →  SDMS.Application/UseCases/
// Phase 3 of the SDMS pipeline.
// Runs IScoringEngine over the AnalysisReport.
// ============================================================

using SDMS.Application.DTOs;
using SDMS.Domain.Models;
using SDMS.Domain.Scoring;

namespace SDMS.Application.UseCases;

public sealed class ScoreUseCase
{
    private readonly IScoringEngine _engine;

    public ScoreUseCase(IScoringEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Scores all files in the report.
    /// </summary>
    /// <param name="report">Output of AnalyzeUseCase.</param>
    /// <param name="weights">
    /// Optional custom weights. Pass null to use <see cref="ScoringWeights.Default"/>.
    /// </param>
    public async Task<ScoringResult> ExecuteAsync(
        AnalysisReport               report,
        ScoringWeights?              weights  = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken            ct       = default)
    {
        progress?.Report(new PipelineProgress
        {
            Phase   = PipelinePhase.Scoring,
            Message = "Scoring files …",
        });

        var started     = DateTime.UtcNow;
        var scoredFiles = await _engine.ScoreAsync(report, weights, ct);
        var elapsed     = DateTime.UtcNow - started;

        double avg  = scoredFiles.Count > 0
            ? scoredFiles.Average(f => f.Score)
            : 0.0;

        progress?.Report(new PipelineProgress
        {
            Phase   = PipelinePhase.Scoring,
            Message = $"Scoring complete — {scoredFiles.Count:N0} files scored (avg {avg:F1}) in {elapsed.TotalSeconds:F2}s",
        });

        return new ScoringResult
        {
            ScoredFiles    = scoredFiles,
            HighScoreCount = scoredFiles.Count(f => f.Score >= 70),
            LowScoreCount  = scoredFiles.Count(f => f.Score < 30),
            AverageScore   = avg,
            Elapsed        = elapsed,
        };
    }
}
