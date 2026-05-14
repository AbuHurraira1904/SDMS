using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SDMS.Application;
using SDMS.Domain.Analysis;
using SDMS.Domain.Brain;
using SDMS.Domain.Execution;
using SDMS.Domain.Models;
using SDMS.Domain.Scanner;
using SDMS.Domain.Scoring;
using SDMS.Domain.PlanEditor;
using SDMS.Infrastructure.Analysis;
using SDMS.Infrastructure.Brain;
using SDMS.Infrastructure.Execution;
using SDMS.Infrastructure.PlanEditor;
using SDMS.Infrastructure.Scanner;
using SDMS.Infrastructure.Scoring;
using SDMS.Infrastructure.Serialization;

namespace SDMS.UI;

/// <summary>
/// Boots an ASP.NET Core minimal-API server inside the WPF process.
/// Listens on http://localhost:5001 — consumed by the React frontend via fetch().
///
/// Endpoints
/// ─────────────────────────────────────────────────────────────
/// POST /api/scan            Scan a directory, return sessionId + BasicInfo
/// GET  /api/scan/{id}       Return full FileTree metadata for a session
/// POST /api/analyze/{id}    Run Brain analysis, return PlanOutput
/// GET  /api/plan/{id}       Return current operations for a session
/// PATCH /api/plan/{id}/op   Update / skip / approve a single operation
/// POST /api/plan/{id}/execute  Execute the finalized plan
/// POST /api/plan/{id}/rollback Rollback the last execution
///
/// SignalR hub:  /api/hubs/progress
///   Client joins group = sessionId to receive live progress events.
/// </summary>
public sealed class LocalApiServer
{
    private WebApplication? _app;
    private const string    ApiBase = "http://localhost:5001";

    // ── In-process session store ──────────────────────────────────────────────
    // Keyed by sessionId (Guid string).  Holds everything produced per scan.
    private readonly SessionStore _sessions = new();

    // ── Brain config ──────────────────────────────────────────────────────────
    private static readonly string BrainFolder =
        Environment.GetEnvironmentVariable("SDMS_BRAIN_FOLDER")
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                                         "..", "..", "..", "..", "Brain"));

    private static readonly string PythonExe  = Path.Combine(BrainFolder, ".venv", "Scripts", "python.exe");
    private static readonly string ApiScript   = Path.Combine(BrainFolder, "api.py");
    private static readonly string BrainUrl    = "http://127.0.0.1:5000";

    // ── Brain process (kept alive for the app lifetime) ───────────────────────
    private BrainProcessManager? _brainManager;

    // ────────────────────────────────────────────────────────────────────────
    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls(ApiBase);

        // ── Services ─────────────────────────────────────────────────────────
        builder.Services.AddSignalR();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            p.WithOrigins("https://sdms.local", "http://localhost:5173")  // local dev too
             .AllowAnyMethod()
             .AllowAnyHeader()
             .AllowCredentials()));

        // Infrastructure — all stateless, safe as singletons
        builder.Services.AddSingleton<IMetaDataExtractor_Interface, MetaDataExtractor>();
        builder.Services.AddSingleton<ISysLinkGuard_Interface,       SysLinkGuard>();
        builder.Services.AddSingleton<IMountDectector_Interface,      MountDetector>();
        builder.Services.AddSingleton<ITreeWalk_Interface>(sp =>
            new TreeWalker(
                sp.GetRequiredService<IMetaDataExtractor_Interface>(),
                sp.GetRequiredService<ISysLinkGuard_Interface>(),
                sp.GetRequiredService<IMountDectector_Interface>()));
        builder.Services.AddSingleton<ITreeSerializer_Interface>(_ => new JSONTreeSerializer(compact: false));
        builder.Services.AddSingleton<IDirectoryScanner>(sp =>
            new Directoryscanner(
                sp.GetRequiredService<ITreeWalk_Interface>(),
                sp.GetRequiredService<ITreeSerializer_Interface>()));
        builder.Services.AddSingleton<IAnalysisEngine, AnalysisEngine>();
        builder.Services.AddSingleton<IScoringEngine,             ScoringEngine>();
        builder.Services.AddSingleton<IExecutionEngine,           ExecutionEngine>();
        builder.Services.AddSingleton<IBrainClient>(_            => new BrainClient(BrainUrl));
        builder.Services.AddSingleton(_sessions);

        _app = builder.Build();

        _app.UseCors();
        _app.MapHub<ProgressHub>("/api/hubs/progress");

        // ── Register all route handlers ───────────────────────────────────────
        ScanEndpoints.Map(_app);
        BrainEndpoints.Map(_app);
        PlanEndpoints.Map(_app);
        ExecuteEndpoints.Map(_app);

        // ── Start the Python brain server ─────────────────────────────────────
        _brainManager = new BrainProcessManager(PythonExe, ApiScript, BrainUrl);
        try { await _brainManager.StartAsync(); }
        catch { /* Brain unavailable — analysis endpoint will return 503 */ }

        await _app.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_app is not null) await _app.StopAsync();
        _brainManager?.Dispose();
    }
}
