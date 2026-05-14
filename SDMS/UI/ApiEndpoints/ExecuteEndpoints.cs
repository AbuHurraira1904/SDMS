using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SDMS.Domain.Execution;
using SDMS.Domain.Models;

namespace SDMS.UI;

/// <summary>
/// POST /api/execute/{sessionId}
///   Runs preflight checks then executes the FinalizedPlan.
///   Returns immediately with { status: "executing" }.
///   Real-time progress streamed over SignalR (type: "execute").
///   When done, SignalR sends { type: "done" } or { type: "error" }.
///
/// GET  /api/execute/{sessionId}/result
///   Returns the full ExecutionLog after execution completes.
///
/// POST /api/execute/{sessionId}/rollback
///   Rolls back the last execution using staged files.
/// </summary>
public static class ExecuteEndpoints
{
    public static void Map(WebApplication app)
    {
        // ── Start execution ─────────────────────────────────────────────────
        app.MapPost("/api/execute/{sessionId}", (
            string         sessionId,
            [FromServices] SessionStore      sessions,
            [FromServices] IExecutionEngine  engine,
            [FromServices] IHubContext<ProgressHub> hub) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "Session not found." });

            if (session.FinalizedPlan is null)
                return Results.BadRequest(new { error = "Plan not finalized. Call /api/plan/{id}/finalize first." });

            // Fire and forget — client tracks progress via SignalR
            _ = RunExecutionAsync(session, engine, hub);

            return Results.Ok(new { status = "executing", sessionId });
        });

        // ── Get execution result ────────────────────────────────────────────
        app.MapGet("/api/execute/{sessionId}/result", (
            string         sessionId,
            [FromServices] SessionStore sessions) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "Session not found." });

            if (session.ExecutionLog is null)
                return Results.Ok(new { status = "executing" });

            var log = session.ExecutionLog;
            return Results.Ok(new
            {
                status       = log.FinalState.ToString(),
                successCount = log.SuccessCount,
                failureCount = log.FailureCount,
                startedAt    = log.StartedAt,
                endedAt      = log.EndedAt,
                results      = log.Results.Select(r => new
                {
                    operationId   = r.OperationId,
                    operationType = r.OperationType.ToString(),
                    success       = r.Success,
                    error         = r.ErrorMessage,
                    stagingPath   = r.StagingPath,
                }),
            });
        });

        // ── Rollback ────────────────────────────────────────────────────────
        app.MapPost("/api/execute/{sessionId}/rollback", async (
            string         sessionId,
            [FromServices] SessionStore      sessions,
            [FromServices] IExecutionEngine  engine,
            [FromServices] IHubContext<ProgressHub> hub) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "Session not found." });

            if (session.ExecutionLog is null)
                return Results.BadRequest(new { error = "No execution to roll back." });

            await ProgressHubExtensions.SendDone(hub, sessionId, "Starting rollback…");

            try
            {
                var rollbackLog = await engine.RollbackAsync(session.ExecutionLog);
                session.ExecutionLog = rollbackLog;
                await ProgressHubExtensions.SendDone(hub, sessionId, "Rollback complete.");
                return Results.Ok(new { status = rollbackLog.FinalState.ToString() });
            }
            catch (Exception ex)
            {
                await ProgressHubExtensions.SendError(hub, sessionId, ex.Message);
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });
    }

    // ── Background execution task ────────────────────────────────────────────
    private static async Task RunExecutionAsync(
        Session              session,
        IExecutionEngine     engine,
        IHubContext<ProgressHub> hub)
    {
        try
        {
            // Preflight
            var validation = await engine.PreflightAsync(session.FinalizedPlan!);
            if (!validation.IsValid)
            {
                var errors = string.Join("; ", validation.Errors);
                await ProgressHubExtensions.SendError(hub, session.Id,
                    $"Preflight failed: {errors}");
                return;
            }

            // Execute with live progress
            var execOptions = new ExecutionOptions
            {
                UseStagingForDeletes = true,
                DryRun               = false,
                RollbackOnFailure    = true,
            };

            var progress = new Progress<(int Completed, int Total, PlannedOperation Current)>(report =>
            {
                _ = ProgressHubExtensions.SendExecuteProgress(
                    hub, session.Id,
                    report.Completed,
                    report.Total,
                    report.Current.Type.ToString(),
                    report.Current.SourcePath ?? "");
            });

            var log = await engine.ExecuteAsync(session.FinalizedPlan!, execOptions, progress);
            session.ExecutionLog = log;

            var msg = log.FailureCount > 0
                ? $"Completed with {log.FailureCount} failure(s). {log.SuccessCount} succeeded."
                : $"All {log.SuccessCount} operations completed successfully.";

            await ProgressHubExtensions.SendDone(hub, session.Id, msg);
        }
        catch (Exception ex)
        {
            await ProgressHubExtensions.SendError(hub, session.Id, ex.Message);
        }
    }
}
