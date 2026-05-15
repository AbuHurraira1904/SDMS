// ============================================================
// ExecutePlanUseCase.cs  →  SDMS.Application/UseCases/
// Phase 6 — Preflight + Execution.
// Wraps IExecutionEngine into two explicit steps the UI calls
// in order: Preflight first, then Execute.
// ============================================================

using SDMS.Application.DTOs;
using SDMS.Domain.Execution;
using SDMS.Domain.Models;
using System.IO;

namespace SDMS.Application.UseCases;

public sealed class ExecutePlanUseCase
{
    private readonly IExecutionEngine _engine;

    public ExecutePlanUseCase(IExecutionEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Validates the plan without touching the filesystem.
    /// The UI should call this and display any errors before showing
    /// the final Execute confirmation.
    /// </summary>
    public async Task<PreflightResult> PreflightAsync(
        FinalizedPlan     plan,
        CancellationToken ct = default)
    {
        var validation = await _engine.PreflightAsync(plan, ct);

        return new PreflightResult
        {
            Validation = validation,
            CanExecute = validation.IsValid,
        };
    }

    /// <summary>
    /// Executes the finalized plan against the filesystem.
    /// Always runs preflight first; throws if preflight fails and DryRun is off.
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(
        FinalizedPlan                plan,
        ExecuteRequest               request,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken            ct       = default)
    {
        // Always run preflight before touching the filesystem
        var preflight = await _engine.PreflightAsync(plan, ct);
        if (!preflight.IsValid && !request.DryRun)
            throw new InvalidOperationException(
                "Preflight failed: " + string.Join("; ", preflight.Errors));

        var options = BuildOptions(request);

        // Wrap execution progress into unified PipelineProgress
        var execProgress = progress is null ? null :
            new Progress<(int Completed, int Total, PlannedOperation Current)>(r =>
                progress.Report(new PipelineProgress
                {
                    Phase     = PipelinePhase.Executing,
                    Message   = $"[{r.Completed}/{r.Total}] {r.Current.Type}: {Path.GetFileName(r.Current.SourcePath ?? r.Current.DestinationPath)}",
                    Completed = r.Completed,
                    Total     = r.Total,
                }));

        progress?.Report(new PipelineProgress
        {
            Phase   = PipelinePhase.Executing,
            Message = $"Executing {plan.OperationCount} operations …",
            Total   = plan.OperationCount,
        });

        var started = DateTime.UtcNow;
        var log     = await _engine.ExecuteAsync(plan, options, execProgress, ct);
        var elapsed = DateTime.UtcNow - started;

        progress?.Report(new PipelineProgress
        {
            Phase   = log.IsFullSuccess ? PipelinePhase.Complete : PipelinePhase.Failed,
            Message = $"Execution finished — {log.SuccessCount} succeeded, {log.FailureCount} failed in {elapsed.TotalSeconds:F2}s",
        });

        return new ExecutionResult
        {
            Log          = log,
            FinalState   = log.FinalState,
            SuccessCount = log.SuccessCount,
            FailureCount = log.FailureCount,
            IsFullSuccess= log.IsFullSuccess,
            Elapsed      = elapsed,
        };
    }

    /// <summary>
    /// Rolls back a previously executed log. Only valid after a PartialFailure.
    /// </summary>
    public async Task<ExecutionLog> RollbackAsync(
        ExecutionLog      log,
        CancellationToken ct = default)
    {
        return await _engine.RollbackAsync(log, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ExecutionOptions BuildOptions(ExecuteRequest req)
    {
        return new ExecutionOptions
        {
            UseStagingForDeletes = req.UseStagingForDeletes,
            DryRun               = req.DryRun,
            RollbackOnFailure    = req.RollbackOnFailure,
            StagingAreaPath      = req.CustomStagingPath ?? new ExecutionOptions().StagingAreaPath,
        };
    }
}
