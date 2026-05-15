// ============================================================
// GeneratePlanUseCase.cs  →  SDMS.Application/UseCases/
// Phase 4 of the SDMS pipeline — the Brain call.
//
// Accepts two strategies:
//   A) IPlanGenerator  — rule-based / future ML in-process generator
//   B) IBrainClient    — the live Python HTTP brain
//
// Exactly one must be injected (constructor overloads).
// If the BrainClient path is taken, this use case also handles:
//   - serialising the FileTree to disk (required by the Python API)
//   - mapping PlanOutput → ProposedPlan via PlanMappingExtensions
// ============================================================

using SDMS.Application.DTOs;
using SDMS.Domain.Brain;
using SDMS.Domain.Models;
using SDMS.Infrastructure.Brain;
using SDMS.Infrastructure.Serialization;
using System.IO;

namespace SDMS.Application.UseCases;

public sealed class GeneratePlanUseCase
{
    private readonly IPlanGenerator? _generator;
    private readonly IBrainClient?   _brainClient;

    // ── Constructor A: in-process generator (rule-based / ML) ────────────────

    public GeneratePlanUseCase(IPlanGenerator generator)
    {
        _generator   = generator;
        _brainClient = null;
    }

    // ── Constructor B: Python HTTP brain ─────────────────────────────────────

    public GeneratePlanUseCase(IBrainClient brainClient)
    {
        _generator   = null;
        _brainClient = brainClient;
    }

    /// <summary>
    /// Generates a ProposedPlan.
    /// </summary>
    /// <param name="report">AnalysisReport from AnalyzeUseCase.</param>
    /// <param name="scoredFiles">ScoredFileNodes from ScoreUseCase.</param>
    /// <param name="tree">
    /// Required when using the BrainClient path — the tree is serialized to a
    /// temp file and the path is forwarded to the Python API.
    /// </param>
    public async Task<PlanResult> ExecuteAsync(
        AnalysisReport               report,
        List<ScoredFileNode>         scoredFiles,
        FileTree?                    tree     = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken            ct       = default)
    {
        progress?.Report(new PipelineProgress
        {
            Phase   = PipelinePhase.GeneratingPlan,
            Message = "Generating reorganization plan …",
        });

        var started = DateTime.UtcNow;
        ProposedPlan plan;

        if (_generator is not null)
        {
            // In-process path
            var genProgress = progress is null ? null :
                new Progress<string>(msg => progress.Report(new PipelineProgress
                {
                    Phase   = PipelinePhase.GeneratingPlan,
                    Message = msg,
                }));

            plan = await _generator.GenerateAsync(report, scoredFiles, genProgress, ct);
        }
        else if (_brainClient is not null)
        {
            // HTTP brain path
            if (tree is null)
                throw new ArgumentNullException(nameof(tree),
                    "FileTree is required when using BrainClient — it must be serialized for the Python API.");

            plan = await CallBrainAsync(report, tree, ct);
        }
        else
        {
            throw new InvalidOperationException(
                "GeneratePlanUseCase has no generator or brain client configured.");
        }

        var elapsed = DateTime.UtcNow - started;

        progress?.Report(new PipelineProgress
        {
            Phase   = PipelinePhase.GeneratingPlan,
            Message = $"Plan generated — {plan.OperationCount} operations in {elapsed.TotalSeconds:F2}s",
        });

        return new PlanResult
        {
            Plan           = plan,
            OperationCount = plan.OperationCount,
            Elapsed        = elapsed,
        };
    }

    // ── Brain HTTP path internals ─────────────────────────────────────────────

    private async Task<ProposedPlan> CallBrainAsync(
        AnalysisReport report,
        FileTree       tree,
        CancellationToken ct)
    {
        // Serialize the tree to a temp file so the Python brain can read it
        var tempPath   = Path.Combine(Path.GetTempPath(), $"sdms_tree_{Guid.NewGuid():N}.json");
        var serializer = new JSONTreeSerializer();
        await serializer.SerializeAsync(tree, tempPath, ct);

        try
        {
            var planOutput = await _brainClient!.AnalyzeAsync(tempPath, ct: ct);

            // Map the raw HTTP DTO → domain model
            var finalizedFromBrain = planOutput.ToFinalizedPlan();

            // Wrap as a ProposedPlan so the PlanEditor can work with it
            return new ProposedPlan
            {
                Operations   = finalizedFromBrain.Operations,
                SourceReport = report,
                GeneratedAt  = DateTime.UtcNow,
            };
        }
        finally
        {
            // Always clean up the temp file even on failure
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
