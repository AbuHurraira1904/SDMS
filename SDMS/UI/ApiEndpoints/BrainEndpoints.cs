using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SDMS.Domain.Brain;
using SDMS.Domain.Models;
using SDMS.Infrastructure.PlanEditor;

namespace SDMS.UI;

/// <summary>
/// POST /api/analyze/{sessionId}
///   Body: { requiredLabels?: string[], ignoredPaths?: string[] }
///   Returns: { sessionId, totalOperations, safeOperations, blockedOperations,
///               operations: [ { id, opType, source, destination, reason,
///                               confidence, importance, status } ] }
///
/// Calls the Python Brain via BrainClient and maps the result into a
/// PlanEditor so subsequent /api/plan calls can modify it.
/// </summary>
public static class BrainEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/analyze/{sessionId}", async (
            string           sessionId,
            [FromBody]  AnalyzeRequest? req,
            [FromServices] SessionStore sessions,
            [FromServices] IBrainClient brain,
            [FromServices] IHubContext<ProgressHub> hub) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "Session not found." });

            if (session.Tree is null || session.TreeJsonPath is null)
                return Results.BadRequest(new { error = "Scan not complete for this session." });

            await ProgressHubExtensions.SendDone(hub, sessionId, "Sending scan to Brain…");

            PlanOutput planOutput;
            try
            {
                planOutput = await brain.AnalyzeAsync(
                    fileTreeJsonPath : session.TreeJsonPath,
                    readContent      : false,
                    allowHidden      : session.ScanOptions?.IncludeHidden   ?? false,
                    allowSystem      : session.ScanOptions?.IncludeSystemFiles ?? false
                );
            }
            catch (Exception ex)
            {
                await ProgressHubExtensions.SendError(hub, sessionId, ex.Message);
                return Results.Problem($"Brain analysis failed: {ex.Message}",
                    statusCode: 503);
            }

            session.PlanOutput = planOutput;

            // Map PlanOutput → ProposedPlan → PlanEditor
            var proposed = planOutput.ToProposedPlan();
            session.Editor = new PlanEditor(proposed);

            await ProgressHubExtensions.SendDone(hub, sessionId,
                $"Brain analysis complete — {planOutput.Operations.Count} operations.");

            return Results.Ok(new
            {
                sessionId,
                scanRoot          = planOutput.ScanRoot,
                totalFilesScanned = planOutput.TotalFilesScanned,
                safety            = new
                {
                    planOutput.Safety.TotalOperations,
                    planOutput.Safety.SafeOperations,
                    planOutput.Safety.BlockedOperations,
                    planOutput.Safety.AffectedFiles,
                },
                operations = planOutput.Operations.Select(MapOp),
            });
        });
    }

    private static object MapOp(PlanOp op) => new
    {
        opType      = op.OpType,
        source      = op.Source,
        destination = op.Destination,
        reason      = op.Reason,
        confidence  = op.Confidence,
        importance  = op.Importance,
        status      = op.Status,
    };
}

/// <summary>Body shape for POST /api/analyze/{sessionId}.</summary>
public sealed record AnalyzeRequest(
    List<string>? RequiredLabels = null,
    List<string>? IgnoredPaths   = null);
