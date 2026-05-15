// ============================================================
// SDMSOrchestrator.cs  →  SDMS.Application/
// The single façade the UI (WPF / WebView2 / CLI) calls.
//
// Responsibilities:
//   - Owns all use case instances
//   - Sequences the pipeline phases
//   - Emits unified PipelineProgress throughout
//   - Exposes EditPlanUseCase after Phase 4 so the UI can drive
//     the review screen without knowing about IPlanEditor
//
// The UI never imports any Infrastructure or Domain namespace
// directly — only SDMS.Application.
// ============================================================

using SDMS.Application.DTOs;
using SDMS.Application.UseCases;
using SDMS.Domain.Brain;
using SDMS.Domain.Execution;
using SDMS.Domain.Models;
using SDMS.Domain.PlanEditor;
using SDMS.Domain.Scoring;

namespace SDMS.Application;

public sealed class SDMSOrchestrator
{
    // ── Use Cases ─────────────────────────────────────────────────────────────

    private readonly ScanUseCase         _scan;
    private readonly AnalyzeUseCase      _analyze;
    private readonly ScoreUseCase        _score;
    private readonly GeneratePlanUseCase _generatePlan;
    private readonly ExecutePlanUseCase  _execute;

    // Factory for creating an EditPlanUseCase once the plan is ready
    private readonly Func<ProposedPlan, EditPlanUseCase> _editFactory;

    // ── State exposed to the UI ───────────────────────────────────────────────

    /// <summary>
    /// Populated after RunToReviewAsync completes.
    /// The UI reads/writes the plan through this object.
    /// </summary>
    public EditPlanUseCase? PlanEditor { get; private set; }

    /// <summary>Last scan result — available for binding in the UI summary panel.</summary>
    public ScanResult?     LastScan     { get; private set; }
    public AnalysisResult? LastAnalysis { get; private set; }
    public ScoringResult?  LastScoring  { get; private set; }
    public PlanResult?     LastPlan     { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="scan">        Phase 1 use case </param>
    /// <param name="analyze">     Phase 2 use case </param>
    /// <param name="score">       Phase 3 use case </param>
    /// <param name="generatePlan">Phase 4 use case </param>
    /// <param name="execute">     Phase 6 use case </param>
    /// <param name="editFactory">
    ///   Factory that creates an EditPlanUseCase for a given ProposedPlan.
    ///   Injected so the orchestrator doesn't new up IPlanEditor directly.
    ///   Typical DI registration:
    ///   <code>plan => new EditPlanUseCase(new PlanEditor(plan), plan)</code>
    /// </param>
    public SDMSOrchestrator(
        ScanUseCase                       scan,
        AnalyzeUseCase                    analyze,
        ScoreUseCase                      score,
        GeneratePlanUseCase               generatePlan,
        ExecutePlanUseCase                execute,
        Func<ProposedPlan, EditPlanUseCase> editFactory)
    {
        _scan         = scan;
        _analyze      = analyze;
        _score        = score;
        _generatePlan = generatePlan;
        _execute      = execute;
        _editFactory  = editFactory;
    }

    // ── Pipeline ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs Phases 1–4 (Scan → Analyze → Score → Brain).
    /// Pauses and hands control back to the UI for plan review.
    /// After this returns, the UI calls <see cref="PlanEditor"/> to
    /// approve/skip/edit operations, then calls <see cref="FinalizeAndExecuteAsync"/>.
    /// </summary>
    public async Task RunToReviewAsync(
        ScanRequest                  request,
        ScoringWeights?              weights  = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken            ct       = default)
    {
        // Phase 1 — Scan
        LastScan = await _scan.ExecuteAsync(request, progress, ct);

        // Phase 2 — Analyze
        LastAnalysis = await _analyze.ExecuteAsync(LastScan.Tree, progress, ct);

        // Phase 3 — Score
        LastScoring = await _score.ExecuteAsync(LastAnalysis.Report, weights, progress, ct);

        // Phase 4 — Brain / Plan Generator
        LastPlan = await _generatePlan.ExecuteAsync(
            LastAnalysis.Report,
            LastScoring.ScoredFiles,
            tree:     LastScan.Tree,
            progress: progress,
            ct:       ct);

        // Hand off to the UI review screen
        PlanEditor = _editFactory(LastPlan.Plan);

        progress?.Report(new PipelineProgress
        {
            Phase   = PipelinePhase.AwaitingUserReview,
            Message = $"Plan ready — {LastPlan.OperationCount} operations awaiting review.",
        });
    }

    /// <summary>
    /// Runs Phases 5–6 (Preflight → Execute).
    /// Call this after the user has finished editing the plan.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="RunToReviewAsync"/> hasn't been called yet,
    /// or if plan validation fails.
    /// </exception>
    public async Task<ExecutionResult> FinalizeAndExecuteAsync(
        ExecuteRequest               request,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken            ct       = default)
    {
        if (PlanEditor is null)
            throw new InvalidOperationException(
                "RunToReviewAsync must complete before FinalizeAndExecuteAsync can be called.");

        // Phase 5 — Finalize (validates + orders operations)
        var finalizedPlan = PlanEditor.Finalize();

        // Phase 6 — Execute
        return await _execute.ExecuteAsync(finalizedPlan, request, progress, ct);
    }

    /// <summary>
    /// Convenience: runs the full pipeline end-to-end without pausing for review.
    /// All operations that the Brain marks Pending are automatically treated as Confirmed.
    /// Useful for automated / headless scenarios.
    /// </summary>
    public async Task<ExecutionResult> RunFullPipelineAsync(
        ScanRequest                  scanRequest,
        ExecuteRequest               executeRequest,
        ScoringWeights?              weights  = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken            ct       = default)
    {
        await RunToReviewAsync(scanRequest, weights, progress, ct);

        // Auto-confirm everything the Brain proposed
        foreach (var op in PlanEditor!.CurrentOperations.ToList())
        {
            if (op.Status == OpStatus.Pending)
                PlanEditor.ApproveOperation(op.Id);
        }

        return await FinalizeAndExecuteAsync(executeRequest, progress, ct);
    }

    /// <summary>
    /// Runs preflight only without executing — lets the UI show a
    /// "what would happen" confirmation screen before the user commits.
    /// </summary>
    public async Task<PreflightResult> PreflightOnlyAsync(CancellationToken ct = default)
    {
        if (PlanEditor is null)
            throw new InvalidOperationException(
                "RunToReviewAsync must complete before PreflightOnlyAsync can be called.");

        var finalizedPlan = PlanEditor.Finalize();
        return await _execute.PreflightAsync(finalizedPlan, ct);
    }
}
