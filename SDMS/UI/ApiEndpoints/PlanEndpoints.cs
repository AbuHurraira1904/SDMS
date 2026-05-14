using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SDMS.Domain.Models;

namespace SDMS.UI;

/// <summary>
/// GET  /api/plan/{sessionId}
///   Returns the current list of operations in the PlanEditor.
///
/// PATCH /api/plan/{sessionId}/op/{operationId}
///   Body: { action: "approve" | "skip" | "updateDestination", destination?: string }
///   Applies a single edit to the plan (approve, skip, or change destination).
///   Supports undo — the PlanEditor snapshot stack is preserved.
///
/// POST /api/plan/{sessionId}/undo
/// POST /api/plan/{sessionId}/redo
///
/// POST /api/plan/{sessionId}/finalize
///   Validates and finalizes the plan. Returns the finalized operation list.
///   Must be called before /api/execute.
/// </summary>
public static class PlanEndpoints
{
    public static void Map(WebApplication app)
    {
        // ── Get current operations ──────────────────────────────────────────
        app.MapGet("/api/plan/{sessionId}", (
            string sessionId,
            [FromServices] SessionStore sessions) =>
        {
            if (!sessions.TryGet(sessionId, out var session) || session.Editor is null)
                return Results.NotFound(new { error = "Plan not found. Run /api/analyze first." });

            return Results.Ok(new
            {
                sessionId,
                canUndo    = session.Editor.CanUndo,
                canRedo    = session.Editor.CanRedo,
                operations = session.Editor.CurrentOperations.Select(MapOp),
            });
        });

        // ── Edit a single operation ─────────────────────────────────────────
        app.MapPatch("/api/plan/{sessionId}/op/{operationId}", (
            string                sessionId,
            Guid                  operationId,
            [FromBody]  OpEditRequest req,
            [FromServices] SessionStore sessions) =>
        {
            if (!sessions.TryGet(sessionId, out var session) || session.Editor is null)
                return Results.NotFound(new { error = "Plan not found." });

            var result = req.Action switch
            {
                "approve"           => session.Editor.SetStatus(operationId, OpStatus.Confirmed),
                "skip"              => session.Editor.SetStatus(operationId, OpStatus.Skipped),
                "updateDestination" => UpdateDestination(session.Editor, operationId, req.Destination),
                _                   => SDMS.Domain.PlanEditor.ValidationResult.Fail($"Unknown action: {req.Action}"),
            };

            if (!result.IsValid)
                return Results.BadRequest(new { errors = result.Errors });

            return Results.Ok(new
            {
                canUndo    = session.Editor.CanUndo,
                canRedo    = session.Editor.CanRedo,
                operations = session.Editor.CurrentOperations.Select(MapOp),
            });
        });

        // ── Undo ───────────────────────────────────────────────────────────
        app.MapPost("/api/plan/{sessionId}/undo", (
            string sessionId,
            [FromServices] SessionStore sessions) =>
        {
            if (!sessions.TryGet(sessionId, out var session) || session.Editor is null)
                return Results.NotFound();

            if (!session.Editor.CanUndo)
                return Results.BadRequest(new { error = "Nothing to undo." });

            session.Editor.Undo();
            return Results.Ok(new
            {
                canUndo    = session.Editor.CanUndo,
                canRedo    = session.Editor.CanRedo,
                operations = session.Editor.CurrentOperations.Select(MapOp),
            });
        });

        // ── Redo ───────────────────────────────────────────────────────────
        app.MapPost("/api/plan/{sessionId}/redo", (
            string sessionId,
            [FromServices] SessionStore sessions) =>
        {
            if (!sessions.TryGet(sessionId, out var session) || session.Editor is null)
                return Results.NotFound();

            if (!session.Editor.CanRedo)
                return Results.BadRequest(new { error = "Nothing to redo." });

            session.Editor.Redo();
            return Results.Ok(new
            {
                canUndo    = session.Editor.CanUndo,
                canRedo    = session.Editor.CanRedo,
                operations = session.Editor.CurrentOperations.Select(MapOp),
            });
        });

        // ── Finalize ────────────────────────────────────────────────────────
        app.MapPost("/api/plan/{sessionId}/finalize", (
            string sessionId,
            [FromServices] SessionStore sessions) =>
        {
            if (!sessions.TryGet(sessionId, out var session) || session.Editor is null)
                return Results.NotFound(new { error = "Plan not found." });

            var validation = session.Editor.ValidatePlan();
            if (!validation.IsValid)
                return Results.BadRequest(new { errors = validation.Errors, warnings = validation.Warnings });

            // Store finalized plan on the session for the execute endpoint
            var proposed = session.PlanOutput!.ToProposedPlan();
            var finalized = session.Editor.Finalize(proposed);
            session.FinalizedPlan = finalized;

            return Results.Ok(new
            {
                sessionId,
                operationCount = finalized.OperationCount,
                operations     = finalized.Operations.Select(MapOp),
            });
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SDMS.Domain.PlanEditor.ValidationResult UpdateDestination(
        SDMS.Infrastructure.PlanEditor.PlanEditor editor,
        Guid operationId,
        string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return SDMS.Domain.PlanEditor.ValidationResult.Fail("Destination cannot be empty.");

        var op = editor.CurrentOperations.FirstOrDefault(o => o.Id == operationId);
        if (op is null)
            return SDMS.Domain.PlanEditor.ValidationResult.Fail("Operation not found.");

        var updated = op.Clone();
        updated.DestinationPath = destination;
        return editor.UpdateOperation(operationId, updated);
    }

    private static object MapOp(PlannedOperation op) => new
    {
        id          = op.Id,
        type        = op.Type.ToString(),
        sourcePath  = op.SourcePath,
        destination = op.DestinationPath,
        reason      = op.Reason,
        confidence  = op.Confidence,
        importance  = op.Importance,
        status      = op.Status.ToString(),
        isUserAdded = op.IsUserAdded,
    };
}

/// <summary>Body shape for PATCH /api/plan/{sessionId}/op/{operationId}.</summary>
public sealed record OpEditRequest(
    string  Action,           // "approve" | "skip" | "updateDestination"
    string? Destination = null);
