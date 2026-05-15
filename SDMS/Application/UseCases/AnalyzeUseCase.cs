// ============================================================
// AnalyzeUseCase.cs  →  SDMS.Application/UseCases/
// Phase 2 of the SDMS pipeline.
// Takes the FileTree from ScanUseCase and runs IAnalysisEngine.
// ============================================================

using SDMS.Application.DTOs;
using SDMS.Domain.Analysis;
using SDMS.Domain.Models;

namespace SDMS.Application.UseCases;

public sealed class AnalyzeUseCase
{
    private readonly IAnalysisEngine _engine;

    public AnalyzeUseCase(IAnalysisEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Runs the analysis engine over the provided <paramref name="tree"/>.
    /// Pure computation — no filesystem access.
    /// </summary>
    public async Task<AnalysisResult> ExecuteAsync(
        FileTree                     tree,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken            ct       = default)
    {
        progress?.Report(new PipelineProgress
        {
            Phase   = PipelinePhase.Analyzing,
            Message = "Analyzing file tree …",
        });

        var started = DateTime.UtcNow;
        var report  = await _engine.AnalyzeAsync(tree, ct);
        var elapsed = DateTime.UtcNow - started;

        var topExtensions = report.FileTypeDistribution
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        progress?.Report(new PipelineProgress
        {
            Phase   = PipelinePhase.Analyzing,
            Message = $"Analysis complete — {report.RequiredLabels.Count} labels suggested in {elapsed.TotalSeconds:F2}s",
        });

        return new AnalysisResult
        {
            Report          = report,
            TopExtensions   = topExtensions,
            CategoryCounts  = new Dictionary<string, int>(tree.BasicInfo.CategoryCounts),
            SuggestedLabels = new List<string>(report.RequiredLabels),
            Elapsed         = elapsed,
        };
    }
}
