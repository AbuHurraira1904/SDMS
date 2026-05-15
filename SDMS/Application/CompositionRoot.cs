// ============================================================
// CompositionRoot.cs  →  SDMS.Application/
// Manual DI composition root — one place where Infrastructure
// types are newed up and wired together.
//
// Both the WPF App.xaml.cs and the CLI Program.cs call
// CompositionRoot.Build() to get a fully configured
// SDMSOrchestrator. Neither caller imports any Infrastructure
// namespace directly.
//
// If you later adopt Microsoft.Extensions.DependencyInjection,
// delete this file and register everything in a ServiceCollection
// extension method instead — the orchestrator and use cases
// don't change at all.
// ============================================================

using SDMS.Application.DTOs;
using SDMS.Application.UseCases;
using SDMS.Domain.Brain;
using SDMS.Domain.Models;
using SDMS.Domain.Scoring;
using SDMS.Infrastructure.Analysis;
using SDMS.Infrastructure.Brain;
using SDMS.Infrastructure.Execution;
using SDMS.Infrastructure.PlanEditor;
using SDMS.Infrastructure.Scanner;
using SDMS.Infrastructure.Scoring;
using SDMS.Infrastructure.Serialization;

namespace SDMS.Application;

public static class CompositionRoot
{
    // ── BrainClient path (default for normal app usage) ──────────────────────

    /// <summary>
    /// Builds a fully wired SDMSOrchestrator that uses the Python HTTP brain.
    /// Call this in App.xaml.cs or Program.cs.
    /// </summary>
    /// <param name="brainUrl">Base URL of the Python Flask brain (default: 127.0.0.1:5000).</param>
    /// <param name="useCompactJson">Produce compact JSON for tree serialization.</param>
    public static SDMSOrchestrator BuildWithBrain(
        string brainUrl      = "http://127.0.0.1:5000",
        bool   useCompactJson = false)
    {
        // ── Infrastructure ────────────────────────────────────────────────────
        var metaExtractor = new MetaDataExtractor();
        var symlinkGuard  = new SysLinkGuard();
        var mountDetector = new MountDetector();
        var walker        = new TreeWalker(metaExtractor, symlinkGuard, mountDetector);
        var serializer    = new JSONTreeSerializer(useCompactJson);
        var scanner       = new Directoryscanner(walker, serializer);
        var brainClient   = new BrainClient(brainUrl);

        // ── Use cases ─────────────────────────────────────────────────────────
        var scanUseCase         = new ScanUseCase(scanner);
        var analyzeUseCase      = new AnalyzeUseCase(new AnalysisEngine());
        var scoreUseCase        = new ScoreUseCase(new ScoringEngine());
        var generatePlanUseCase = new GeneratePlanUseCase((IBrainClient)brainClient);
        var executeUseCase      = new ExecutePlanUseCase(new ExecutionEngine());

        return new SDMSOrchestrator(
            scan:         scanUseCase,
            analyze:      analyzeUseCase,
            score:        scoreUseCase,
            generatePlan: generatePlanUseCase,
            execute:      executeUseCase,
            editFactory:  plan => new EditPlanUseCase(new PlanEditor(plan), plan));
    }

    // ── MsgPack serializer variant ────────────────────────────────────────────

    /// <summary>
    /// Same as BuildWithBrain but uses MessagePack for tree serialization.
    /// Faster and smaller on disk — useful when trees are very large.
    /// </summary>
    public static SDMSOrchestrator BuildWithBrainMsgPack(
        string brainUrl = "http://127.0.0.1:5000")
    {
        var metaExtractor = new MetaDataExtractor();
        var symlinkGuard  = new SysLinkGuard();
        var mountDetector = new MountDetector();
        var walker        = new TreeWalker(metaExtractor, symlinkGuard, mountDetector);
        var serializer    = new Msgtreeserializer();
        var scanner       = new Directoryscanner(walker, serializer);
        var brainClient   = new BrainClient(brainUrl);

        return new SDMSOrchestrator(
            scan:         new ScanUseCase(scanner),
            analyze:      new AnalyzeUseCase(new AnalysisEngine()),
            score:        new ScoreUseCase(new ScoringEngine()),
            generatePlan: new GeneratePlanUseCase((IBrainClient)brainClient),
            execute:      new ExecutePlanUseCase(new ExecutionEngine()),
            editFactory:  plan => new EditPlanUseCase(new PlanEditor(plan), plan));
    }

    // ── BrainProcessManager helper ────────────────────────────────────────────

    /// <summary>
    /// Creates a BrainProcessManager that controls the Python process lifecycle.
    /// Call StartAsync() during app startup and Dispose() on shutdown.
    /// </summary>
    public static BrainProcessManager CreateBrainProcessManager(
        string pythonExe,
        string apiScriptPath,
        string brainUrl = "http://127.0.0.1:5000")
    {
        return new BrainProcessManager(pythonExe, apiScriptPath, brainUrl);
    }
}
