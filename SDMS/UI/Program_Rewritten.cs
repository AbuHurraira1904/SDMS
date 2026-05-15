/*
// ============================================================
// Program.cs  →  SDMS.UI/  (rewritten)
// CLI entry point after Application layer extraction.
//
// This file now contains ZERO business logic and ZERO
// Infrastructure imports. It only:
//   1. Parses CLI args into Application-layer DTOs
//   2. Calls CompositionRoot.Build() to get an orchestrator
//   3. Subscribes to PipelineProgress for console output
//   4. Drives the pipeline phases
// ============================================================

using System.IO;
using SDMS.Application;
using SDMS.Application.DTOs;
using SDMS.Domain.Models;

// ── Argument parsing ──────────────────────────────────────────────────────────

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

string root         = args[0];
string format       = "json";
bool   compact      = false;
bool   hidden       = false;
bool   system       = false;
bool   symlinks     = false;
bool   dryRun       = false;
bool   autoConfirm  = false;    // skip interactive review
int?   maxDepth     = null;
long?  maxFileBytes = null;
string brainUrl     = "http://127.0.0.1:5000";

for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--format"   when i + 1 < args.Length:
            format = args[++i];
            if (format is not ("json" or "msgpack"))
            { Console.Error.WriteLine("--format must be json or msgpack."); return 1; }
            break;
        case "--compact":     compact     = true; break;
        case "--hidden":      hidden      = true; break;
        case "--system":      system      = true; break;
        case "--symlinks":    symlinks    = true; break;
        case "--dry-run":     dryRun      = true; break;
        case "--yes":         autoConfirm = true; break;
        case "--depth"    when i + 1 < args.Length:
            maxDepth    = int.Parse(args[++i]); break;
        case "--maxsize"  when i + 1 < args.Length:
            maxFileBytes = long.Parse(args[++i]); break;
        case "--brain-url" when i + 1 < args.Length:
            brainUrl    = args[++i]; break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

// ── Build orchestrator via CompositionRoot ────────────────────────────────────

var orchestrator = format == "msgpack"
    ? CompositionRoot.BuildWithBrainMsgPack(brainUrl)
    : CompositionRoot.BuildWithBrain(brainUrl, compact);

// ── Progress handler ──────────────────────────────────────────────────────────

var progress = new Progress<PipelineProgress>(p =>
{
    var prefix = $"[{p.Phase,-18}]";

    if (p.Phase == PipelinePhase.Scanning && p.FilesCount.HasValue)
    {
        // Overwrite line so the terminal doesn't scroll during long scans
        Console.Write($"\r{prefix} {p.FilesCount:N0} files … {Truncate(p.Message, 55)}   ");
        return;
    }

    if (p.Phase == PipelinePhase.Executing && p.Completed.HasValue)
    {
        Console.Write($"\r{prefix} [{p.Completed}/{p.Total}] {Truncate(p.Message, 50)}   ");
        return;
    }

    // All other phases — normal newline output
    if (p.Phase == PipelinePhase.Scanning) Console.WriteLine(); // end overwrite line
    Console.WriteLine($"{prefix} {p.Message}");
});

// ── Scan request ──────────────────────────────────────────────────────────────

var scanRequest = new ScanRequest
{
    RootPath           = root,
    IncludeHidden      = hidden,
    IncludeSystemFiles = system,
    FollowSymlinks     = symlinks,
    MaxDepth           = maxDepth,
    MaxFileSizeBytes   = maxFileBytes,
};

var executeRequest = new ExecuteRequest
{
    DryRun               = dryRun,
    UseStagingForDeletes = true,
    RollbackOnFailure    = true,
};

// ── Run pipeline ──────────────────────────────────────────────────────────────

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    // Phases 1–4: Scan → Analyze → Score → Brain
    await orchestrator.RunToReviewAsync(scanRequest, progress: progress, ct: cts.Token);

    // Print scan + analysis summary
    PrintScanSummary(orchestrator.LastScan!);
    PrintAnalysisSummary(orchestrator.LastAnalysis!);
    PrintScoringSummary(orchestrator.LastScoring!);
    PrintPlanSummary(orchestrator.LastPlan!);

    // Phase 5: User review (or auto-confirm)
    if (!autoConfirm)
    {
        Console.WriteLine();
        Console.WriteLine("Review the plan above. Press ENTER to execute, or Ctrl+C to abort.");
        Console.ReadLine();

        // Approve all pending operations
        foreach (var op in orchestrator.PlanEditor!.CurrentOperations.ToList())
        {
            if (op.Status == OpStatus.Pending)
                orchestrator.PlanEditor.ApproveOperation(op.Id);
        }
    }
    else
    {
        // Auto-confirm all pending
        foreach (var op in orchestrator.PlanEditor!.CurrentOperations.ToList())
        {
            if (op.Status == OpStatus.Pending)
                orchestrator.PlanEditor.ApproveOperation(op.Id);
        }
    }

    // Preflight only — show any issues before committing
    var preflight = await orchestrator.PreflightOnlyAsync(cts.Token);
    if (!preflight.CanExecute)
    {
        Console.Error.WriteLine("\n[preflight] Failed:");
        foreach (var err in preflight.Validation.Errors)
            Console.Error.WriteLine($"  • {err}");
        return 4;
    }

    // Phase 6: Execute
    var result = await orchestrator.FinalizeAndExecuteAsync(executeRequest, progress, cts.Token);
    Console.WriteLine();
    PrintExecutionResult(result);

    return result.IsFullSuccess ? 0 : 5;
}
catch (DirectoryNotFoundException ex)
{
    Console.Error.WriteLine($"\n[scan] Error: {ex.Message}");
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("\n[pipeline] Cancelled by user.");
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\n[pipeline] Unexpected error: {ex.Message}");
    return 99;
}

// ── Console helpers ───────────────────────────────────────────────────────────

static string Truncate(string s, int max) =>
    s.Length <= max ? s : "…" + s[^(max - 1)..];

static string FormatBytes(long bytes) => bytes switch
{
    < 1_024         => $"{bytes} B",
    < 1_048_576     => $"{bytes / 1024.0:F1} KB",
    < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
    _               => $"{bytes / 1_073_741_824.0:F2} GB",
};

static void PrintScanSummary(ScanResult s)
{
    Console.WriteLine();
    Console.WriteLine($"  ┌─ Scan ───────────────────────────────────────");
    Console.WriteLine($"  │  Root      : {s.Tree.ScanRootPath}");
    Console.WriteLine($"  │  Files     : {s.TotalFiles:N0}");
    Console.WriteLine($"  │  Dirs      : {s.TotalDirs:N0}");
    Console.WriteLine($"  │  Total size: {FormatBytes(s.TotalBytes)}");
    Console.WriteLine($"  │  Time      : {s.Elapsed.TotalSeconds:F2}s");
    Console.WriteLine($"  └─────────────────────────────────────────────");
}

static void PrintAnalysisSummary(AnalysisResult a)
{
    Console.WriteLine();
    Console.WriteLine($"  ┌─ Analysis ───────────────────────────────────");
    Console.WriteLine($"  │  Suggested labels: {string.Join(", ", a.SuggestedLabels)}");
    Console.WriteLine($"  │  Top extensions:");
    foreach (var kv in a.TopExtensions.Take(5))
        Console.WriteLine($"  │    .{kv.Key,-12} {kv.Value,6:N0} files");
    Console.WriteLine($"  │  Time: {a.Elapsed.TotalSeconds:F2}s");
    Console.WriteLine($"  └─────────────────────────────────────────────");
}

static void PrintScoringSummary(ScoringResult s)
{
    Console.WriteLine();
    Console.WriteLine($"  ┌─ Scoring ────────────────────────────────────");
    Console.WriteLine($"  │  Files scored : {s.ScoredFiles.Count:N0}");
    Console.WriteLine($"  │  Average score: {s.AverageScore:F1}");
    Console.WriteLine($"  │  High (≥70)   : {s.HighScoreCount:N0}");
    Console.WriteLine($"  │  Low  (<30)   : {s.LowScoreCount:N0}");
    Console.WriteLine($"  │  Time         : {s.Elapsed.TotalSeconds:F2}s");
    Console.WriteLine($"  └─────────────────────────────────────────────");
}

static void PrintPlanSummary(PlanResult p)
{
    Console.WriteLine();
    Console.WriteLine($"  ┌─ Proposed Plan ──────────────────────────────");
    Console.WriteLine($"  │  Operations: {p.OperationCount}");
    Console.WriteLine($"  │  Generated : {p.Elapsed.TotalSeconds:F2}s");
    foreach (var op in p.Plan.Operations.Take(10))
        Console.WriteLine($"  │  [{op.Type,-12}] {Path.GetFileName(op.SourcePath ?? op.DestinationPath)}");
    if (p.OperationCount > 10)
        Console.WriteLine($"  │  … and {p.OperationCount - 10} more");
    Console.WriteLine($"  └─────────────────────────────────────────────");
}

static void PrintExecutionResult(ExecutionResult r)
{
    Console.WriteLine();
    Console.WriteLine($"  ┌─ Execution ──────────────────────────────────");
    Console.WriteLine($"  │  State  : {r.FinalState}");
    Console.WriteLine($"  │  Success: {r.SuccessCount}");
    Console.WriteLine($"  │  Failed : {r.FailureCount}");
    Console.WriteLine($"  │  Time   : {r.Elapsed.TotalSeconds:F2}s");
    Console.WriteLine($"  └─────────────────────────────────────────────");
}

static void PrintHelp() => Console.WriteLine("""
    SDMS — Smart Directory Management System
    =========================================
    Usage: sdms <root-path> [options]

    Arguments:
      <root-path>            Directory to reorganize (required)

    Options:
      --hidden               Include hidden files/dirs
      --system               Include system files/dirs
      --symlinks             Follow symbolic links
      --depth   <n>          Max recursion depth
      --maxsize <bytes>      Skip files larger than this
      --format  <fmt>        json | msgpack  (default: json)
      --compact              Compact JSON output
      --dry-run              Simulate — no filesystem changes
      --yes                  Skip interactive review, auto-confirm all ops
      --brain-url <url>      Python brain URL (default: http://127.0.0.1:5000)
      -h, --help             Show this help

    Examples:
      sdms C:\Users\Me\Downloads --dry-run
      sdms /home/user/docs --hidden --yes
      sdms D:\Projects --depth 4 --format msgpack
    """);
    */
