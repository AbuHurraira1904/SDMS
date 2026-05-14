using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SDMS.Application;
using SDMS.Domain.Scanner;

namespace SDMS.UI;

/// <summary>
/// POST /api/scan
///   Body: { directoryPath, recursionLevel, includeHidden, includeSystem, followSymlinks }
///   Returns: { sessionId, totalFiles, totalDirectories, totalSizeBytes,
///               categoryBreakdown, extensionBreakdown, oldestFile, newestFile,
///               immediateSubDirs, skippedPaths }
///
/// The scan runs in the background and streams progress over SignalR.
/// The HTTP response is returned immediately with the session ID;
/// the React app subscribes to the SignalR group to track live progress,
/// then calls GET /api/scan/{id} when done to get the full result.
/// </summary>
public static class ScanEndpoints
{
    public static void Map(WebApplication app)
    {
        // ── Start a scan ────────────────────────────────────────────────────
        app.MapPost("/api/scan", async (
            [FromBody]  ScanRequest       req,
            [FromServices] IDirectoryScanner scanner,
            [FromServices] SessionStore    sessions,
            [FromServices] IHubContext<ProgressHub> hub) =>
        {
            if (!Directory.Exists(req.DirectoryPath))
                return Results.BadRequest(new { error = "Directory does not exist." });

            var session = sessions.Create();
            session.ScanRoot = req.DirectoryPath;

            var options = new ScanOptions
            {
                IncludeHidden      = req.IncludeHidden,
                IncludeSystemFiles = req.IncludeSystem,
                FollowSymlinks     = req.FollowSymlinks,
                MaxDepth           = req.RecursionLevel > 0 ? req.RecursionLevel : null,
            };
            session.ScanOptions = options;

            // Return sessionId immediately; scan runs in background
            _ = RunScanAsync(session, scanner, hub, options, req.DirectoryPath);

            return Results.Ok(new { sessionId = session.Id, status = "scanning" });
        });

        // ── Poll / fetch scan results ───────────────────────────────────────
        app.MapGet("/api/scan/{sessionId}", (
            string sessionId,
            [FromServices] SessionStore sessions) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "Session not found." });

            if (session.Tree is null)
                return Results.Ok(new { status = "scanning" });

            var info = session.Tree.BasicInfo;
            return Results.Ok(new
            {
                status             = "done",
                sessionId,
                scanRoot           = session.ScanRoot,
                totalFiles         = info.TotalFiles,
                totalDirectories   = info.TotalDirectories,
                totalSizeBytes     = info.TotalSizeBytes,
                totalIgnoredFiles  = info.TotalIgnoredFiles,
                categoryBreakdown  = info.CategoryCounts,
                categorySizes      = info.CategorySizes,
                extensionBreakdown = info.ExtensionCounts,
                extensionSizes     = info.ExtensionSizes,
                oldestFile         = info.OldestFile == DateTime.MaxValue ? null : (DateTime?)info.OldestFile,
                newestFile         = info.NewestFile == DateTime.MinValue ? null : (DateTime?)info.NewestFile,
                immediateSubDirs   = info.ImmediateSubDirs,
                hiddenPaths        = info.HiddenPaths,
                systemPaths        = info.SystemPaths,
                skippedPaths       = info.SkippedPaths,
            });
        });
    }

    // ── Background scan task ─────────────────────────────────────────────────
    private static async Task RunScanAsync(
        Session              session,
        IDirectoryScanner    scanner,
        IHubContext<ProgressHub> hub,
        ScanOptions          options,
        string               rootPath)
    {
        try
        {
            var progress = new Progress<(int FilesScanned, string CurrentPath)>(report =>
            {
                // Fire-and-forget SignalR push — don't await in a sync lambda
                _ = ProgressHubExtensions.SendScanProgress(
                    hub, session.Id, report.FilesScanned, report.CurrentPath);
            });

            var tree = await scanner.ScanAsync(rootPath, options, progress);
            session.Tree = tree;

            // Save to disk so the Brain can read it by path
            if (scanner is Directoryscanner ds)
            {
                var outPath = Path.Combine(
                    Path.GetTempPath(), $"sdms_{session.Id}.json");
                session.TreeJsonPath = await ds.SaveAsync(tree, outPath);
            }

            await ProgressHubExtensions.SendDone(hub, session.Id,
                $"Scan complete — {tree.BasicInfo.TotalFiles:N0} files.");
        }
        catch (Exception ex)
        {
            await ProgressHubExtensions.SendError(hub, session.Id, ex.Message);
        }
    }
}

/// <summary>Body shape for POST /api/scan.</summary>
public sealed record ScanRequest(
    string DirectoryPath,
    int    RecursionLevel  = 0,   // 0 = unlimited
    bool   IncludeHidden   = false,
    bool   IncludeSystem   = false,
    bool   FollowSymlinks  = false);
