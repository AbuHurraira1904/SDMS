using System.IO;
using SDMS.Application;
using SDMS.Domain.Analysis;
using SDMS.Domain.Scanner;
using SDMS.Infrastructure.Scanner;
using SDMS.Infrastructure.Serialization;
using SDMS.Domain.Models;
using SDMS.Infrastructure.Execution;
using SDMS.Domain.Execution;
using SDMS.Domain.Brain;
using SDMS.Domain.Scoring;
using SDMS.Infrastructure.Analysis;
using SDMS.Infrastructure.Brain;
using SDMS.Infrastructure.PlanEditor;
using SDMS.Infrastructure.Scoring;

// ── Argument parsing ──────────────────────────────────────────────────────────

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

string root         = args[0];
string output       = "filetree.json";
string format       = "json";
bool   compact      = false;
bool   hidden       = false;
bool   system       = false;
bool   symlinks     = false;
bool   dryRun       = false;
int?   maxDepth     = null;
long?  maxFileBytes = null;

for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--output"  when i + 1 < args.Length: output = args[++i]; break;
        case "--format"  when i + 1 < args.Length:
            format = args[++i];
            if (format is not ("json" or "msgpack"))
            { Console.Error.WriteLine("--format must be json or msgpack."); return 1; }
            break;
        case "--compact":  compact  = true; break;
        case "--hidden":   hidden   = true; break;
        case "--system":   system   = true; break;
        case "--symlinks": symlinks = true; break;
        case "--dry-run":  dryRun   = true; break;
        case "--depth"   when i + 1 < args.Length:
            maxDepth = int.Parse(args[++i]); break;
        case "--maxsize" when i + 1 < args.Length:
            maxFileBytes = long.Parse(args[++i]); break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

// ── Brain config ──────────────────────────────────────────────────────────────
// BrainFolder is the directory containing api.py and the .venv folder.
// Change this to your actual Brain folder path, or read from an env var.

string brainFolder = Environment.GetEnvironmentVariable("SDMS_BRAIN_FOLDER")
    ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Brain");

brainFolder = Path.GetFullPath(brainFolder);

string pythonExe = Path.Combine(brainFolder, ".venv", "Scripts", "python.exe");
string apiScript = Path.Combine(brainFolder, "api.py");
string brainUrl  = "http://127.0.0.1:5000";

// ── Dependency wiring ─────────────────────────────────────────────────────────

var options = new ScanOptions
{
    FollowSymlinks     = symlinks,
    IncludeHidden      = hidden,
    IncludeSystemFiles = system,
    MaxDepth           = maxDepth,
    MaxFileSizeBytes   = maxFileBytes,
};

IMetaDataExtractor_Interface metaExtractor = new MetaDataExtractor();
ISysLinkGuard_Interface      symlinkGuard  = new SysLinkGuard();
IMountDectector_Interface    mountDetector = new MountDetector();
ITreeWalk_Interface          walker        = new TreeWalker(metaExtractor, symlinkGuard, mountDetector);

ITreeSerializer_Interface serializer = format == "msgpack"
    ? new Msgtreeserializer()
    : new JSONTreeSerializer(compact);

IDirectoryScanner scanner = new Directoryscanner(walker, serializer);

// ── Progress display ──────────────────────────────────────────────────────────

var progress = new Progress<(int FilesScanned, string CurrentPath)>(report =>
{
    Console.Write($"\r[scanner] {report.FilesScanned:N0} files … {TruncatePath(report.CurrentPath, 60)}   ");
});

// ── Step 1: Scan ──────────────────────────────────────────────────────────────

Console.WriteLine($"[scanner] Starting scan of '{root}' …");

FileTree tree;
try
{
    tree = await scanner.ScanAsync(root, options, progress);
}
catch (DirectoryNotFoundException ex)
{
    Console.Error.WriteLine($"\n[scanner] Error: {ex.Message}");
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("\n[scanner] Scan cancelled.");
    return 3;
}

Console.WriteLine();

var elapsed = DateTime.UtcNow - tree.ScannedAt;
Console.WriteLine(
    $"[scanner] Done — {tree.BasicInfo.TotalFiles:N0} files, " +
    $"{tree.BasicInfo.TotalDirectories:N0} dirs, " +
    $"{tree.BasicInfo.TotalSizeBytes:N0} bytes " +
    $"in {elapsed.TotalSeconds:F2}s");

if (tree.BasicInfo.SkippedPaths.Count > 0)
{
    Console.WriteLine($"[scanner] {tree.BasicInfo.SkippedPaths.Count} path(s) skipped (permission/IO errors):");
    foreach (var p in tree.BasicInfo.SkippedPaths.Take(10))
        Console.WriteLine($"  • {p}");
    if (tree.BasicInfo.SkippedPaths.Count > 10)
        Console.WriteLine($"  … and {tree.BasicInfo.SkippedPaths.Count - 10} more.");
}

// ── Step 2: Save FileTree ─────────────────────────────────────────────────────

string savedPath;
if (scanner is Directoryscanner ds)
{
    savedPath = await ds.SaveAsync(tree, output);
    Console.WriteLine($"[scanner] FileTree written → {savedPath}");
}
else
{
    Console.Error.WriteLine("[scanner] Could not save FileTree — unexpected scanner type.");
    return 4;
}

// ── Step 3: Start brain process ───────────────────────────────────────────────

using var brainManager = new BrainProcessManager(pythonExe, apiScript, brainUrl);

try
{
    Console.WriteLine("[brain] Starting Python brain server…");
    Console.WriteLine($"[brain] python  → {pythonExe}");
    Console.WriteLine($"[brain] api.py  → {apiScript}");
    await brainManager.StartAsync();
}
catch (TimeoutException ex)
{
    Console.Error.WriteLine($"[brain] {ex.Message}");
    return 5;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[brain] Failed to start brain process: {ex.Message}");
    Console.Error.WriteLine($"[brain] Check that python.exe exists at: {pythonExe}");
    return 5;
}

// ── Step 4: Send FileTree path to brain, get plan back ───────────────────────

Console.WriteLine("[brain] Sending scan to brain for analysis…");

using var brainClient = new BrainClient(brainUrl);

PlanOutput planOutput;
try
{
    planOutput = await brainClient.AnalyzeAsync(
        fileTreeJsonPath : Path.GetFullPath(savedPath),
        readContent      : false,
        allowHidden      : hidden,
        allowSystem      : system
    );
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[brain] Analysis failed: {ex.Message}");
    return 6;
}
catch (InvalidDataException ex)
{
    Console.Error.WriteLine($"[brain] Bad response from brain: {ex.Message}");
    return 7;
}

Console.WriteLine($"[brain] Plan received — {planOutput.Operations.Count} operations, " +
                  $"{planOutput.Safety.SafeOperations} safe, " +
                  $"{planOutput.Safety.BlockedOperations} blocked.");

Console.WriteLine("\n=== PLAN OUTPUT ===");
Console.WriteLine($"Scan Root: {planOutput?.ScanRoot}");
Console.WriteLine($"Total Files: {planOutput?.TotalFilesScanned}");

Console.WriteLine("\n=== SAFETY ===");
Console.WriteLine($"Total Ops: {planOutput?.Safety.TotalOperations}");
Console.WriteLine($"Safe Ops: {planOutput?.Safety.SafeOperations}");
Console.WriteLine($"Blocked Ops: {planOutput?.Safety.BlockedOperations}");

Console.WriteLine("\n=== OPERATIONS ===");
foreach (var op in planOutput?.Operations ?? [])
{
    Console.WriteLine("--------------------------------");
    Console.WriteLine($"Type        : {op.OpType}");
    Console.WriteLine($"Source      : {op.Source}");
    Console.WriteLine($"Destination : {op.Destination}");
    Console.WriteLine($"Reason      : {op.Reason}");
    Console.WriteLine($"Confidence  : {op.Confidence}");
    Console.WriteLine($"Importance  : {op.Importance}");
    Console.WriteLine($"Status      : {op.Status}");
}

// ── Step 5: Map PlanOutput → FinalizedPlan ────────────────────────────────────

FinalizedPlan plan;
try
{
    plan = planOutput.ToFinalizedPlan();
    Console.Write(plan);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[mapping] Failed to map plan: {ex.Message}");
    return 8;
}

Console.WriteLine($"[mapping] Mapped {plan.Operations.Count} operations.");

// ── Step 6: Print operation summary ──────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("  ┌─ Planned Operations ─────────────────────────");
var grouped = plan.Operations
    .GroupBy(op => op.Type)
    .OrderByDescending(g => g.Count());
foreach (var g in grouped)
    Console.WriteLine($"  │  {g.Key,-14} {g.Count(),4} ops");
Console.WriteLine("  └─────────────────────────────────────────────");
Console.WriteLine();

if (dryRun)
{
    Console.WriteLine("[execution] Dry-run mode — skipping execution.");
    return 0;
}

// ── Step 7: Preflight ─────────────────────────────────────────────────────────

IExecutionEngine executionEngine = new ExecutionEngine();

Console.WriteLine("[execution] Running preflight checks…");
var validation = await executionEngine.PreflightAsync(plan);

if (!validation.IsValid)
{
    Console.Error.WriteLine("[execution] Preflight failed — aborting.");
    foreach (var err in validation.Errors)
        Console.Error.WriteLine($"  • {err}");
    return 9;
}

if (validation.Warnings.Count > 0)
{
    foreach (var w in validation.Warnings)
        Console.WriteLine($"  [WARN] {w}");
}

Console.WriteLine("[execution] Preflight passed.");

// ── Step 8: Execute ───────────────────────────────────────────────────────────

var execOptions = new ExecutionOptions
{
    UseStagingForDeletes = true,
    DryRun               = false,
    RollbackOnFailure    = true,
};

var execProgress = new Progress<(int Completed, int Total, PlannedOperation Current)>(report =>
{
    Console.Write($"\r[execution] {report.Completed}/{report.Total} — {report.Current.Type}: {TruncatePath(report.Current.SourcePath ?? "", 50)}   ");
});

Console.WriteLine("[execution] Executing plan…");
var log = await executionEngine.ExecuteAsync(plan, execOptions, execProgress);
Console.WriteLine();

// ── Step 9: Report results ────────────────────────────────────────────────────

Console.WriteLine($"[execution] Finished — {log.SuccessCount} succeeded, {log.FailureCount} failed. State: {log.FinalState}");

if (log.FailureCount > 0)
{
    Console.WriteLine();
    Console.Error.WriteLine("[execution] Failed operations:");
    foreach (var result in log.Results.Where(r => !r.Success))
        Console.Error.WriteLine($"  • [{result.OperationType}] {result.ErrorMessage}");
    return 10;
}

return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

static string TruncatePath(string path, int max) =>
    path.Length <= max ? path : "…" + path[^(max - 1)..];

static void PrintHelp() => Console.WriteLine("""
    SDMS — Directory Scanner + Brain
    ==================================
    Usage: scanner <root-path> [options]

    Arguments:
      <root-path>          Directory to scan (required)

    Options:
      --output  <path>     FileTree output path    (default: filetree.json)
      --format  <fmt>      json | msgpack           (default: json)
      --compact            Compact JSON, no indent
      --hidden             Include hidden files/dirs
      --system             Include system files/dirs
      --symlinks           Follow symbolic links
      --depth   <n>        Max recursion depth
      --maxsize <bytes>    Skip files larger than this
      --dry-run            Map and validate plan but skip execution
      -h, --help           Show this help

    Requires:
      Brain folder set via SDMS_BRAIN_FOLDER env var, or auto-resolved
      relative to the executable.

    Examples:
      scanner /home/user --depth 5 --hidden --output tree.json
      scanner C:\Users   --format msgpack --output tree.msgpack
      scanner /srv/data  --maxsize 104857600   # skip files > 100 MB
    """);

static string FormatBytes(long bytes) => bytes switch
{
    < 1_024         => $"{bytes} B",
    < 1_048_576     => $"{bytes / 1024.0:F1} KB",
    < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
    _               => $"{bytes / 1_073_741_824.0:F2} GB",
};

static void Warn(string msg) => WriteColored("  [WARN] ", ConsoleColor.Yellow, msg);

static void WriteColored(string prefix, ConsoleColor color, string msg)
{
    Console.ForegroundColor = color;
    Console.Write(prefix);
    Console.ResetColor();
    Console.WriteLine(msg);
}