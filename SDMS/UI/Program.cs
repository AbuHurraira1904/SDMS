// ============================================================
// Program.cs  →  SDMS.UI/
// CLI entry point — wires all dependencies manually and calls
// IDirectoryScanner.ScanAsync().
// Zero business logic lives here. Swap for Microsoft.Extensions.DI
// or any other container without touching any other file.
// ============================================================

using System.IO;
using SDMS.Application;
using SDMS.Domain.Scanner;
using SDMS.Domain.Analysis;
using SDMS.Infrastructure.Scanner;
using SDMS.Infrastructure.Analysis;
using SDMS.Infrastructure.Serialization;
using SDMS.Domain.Models;
using SDMS.Infrastructure.Execution;
using SDMS.Domain.Execution;
using SDMS.Domain.Scoring;
using SDMS.Infrastructure.PlanEditor;
using SDMS.Infrastructure.Scoring;

// ── Argument parsing ──────────────────────────────────────────────────────────

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

string root           = args[0];
string output         = "filetree.json";
string format         = "json";
bool   compact        = false;
bool   hidden         = false;
bool   system         = false;
bool   symlinks       = false;
int?   maxDepth       = null;
long?  maxFileBytes   = null;

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
        case "--depth"   when i + 1 < args.Length:
            maxDepth = int.Parse(args[++i]); break;
        case "--maxsize" when i + 1 < args.Length:
            maxFileBytes = long.Parse(args[++i]); break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

// ── Dependency wiring ─────────────────────────────────────────────────────────

var options = new ScanOptions
{
    FollowSymlinks    = symlinks,
    IncludeHidden     = hidden,
    IncludeSystemFiles = system,
    MaxDepth          = maxDepth,
    MaxFileSizeBytes  = maxFileBytes,
};

// Infrastructure layer — all OS-touching types.
IMetaDataExtractor_Interface metaExtractor = new MetaDataExtractor();
ISysLinkGuard_Interface      symlinkGuard  = new SysLinkGuard();
IMountDectector_Interface    mountDetector = new MountDetector();
ITreeWalk_Interface          walker        = new TreeWalker(metaExtractor, symlinkGuard, mountDetector);

ITreeSerializer_Interface serializer = format == "msgpack"
    ? new Msgtreeserializer()
    : new JSONTreeSerializer(compact);

// Application layer — orchestrating facade.
IDirectoryScanner scanner = new Directoryscanner(walker, serializer);

// ── Progress display ──────────────────────────────────────────────────────────

var progress = new Progress<(int FilesScanned, string CurrentPath)>(report =>
{
    // Overwrite the current line so the terminal doesn't scroll on large scans.
    Console.Write($"\r[scanner] {report.FilesScanned:N0} files … {TruncatePath(report.CurrentPath, 60)}   ");
});

// ── Run the scan ──────────────────────────────────────────────────────────────

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

// Clear the progress line.
Console.WriteLine();

var elapsed = DateTime.UtcNow - tree.ScannedAt;
Console.WriteLine(
    $"[scanner] Done — " +
    $"in {elapsed.TotalSeconds:F2}s");

// 2. Analysis Engine (Compute Bound)
IAnalysisEngine analysisEngine = new AnalysisEngine();
AnalysisReport report;
try
{
    report = await analysisEngine.AnalyzeAsync(tree);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("\n[analysis] Analysis cancelled.");
    return 3;
}

// 3. Result Review
Console.WriteLine(
    $"[analysis] Done — for {report.SourceTree.ScanRootPath} " + 
    $"{report.FileTypeDistribution.Values.Sum()} files, " +
    $"{tree.BasicInfo.TotalDirectories:N0} dirs, " +
    $"{tree.BasicInfo.TotalSizeBytes:N0} bytes, " +
    $"{tree.BasicInfo.TotalIgnoredFiles:N0} ignored ");

Console.WriteLine($"Required Labels: {string.Join(", ", report.RequiredLabels)}");

// ── Analysis ──────────────────────────────────────────────────────────
var a = tree.BasicInfo;  // shorthand

// Category breakdown
Console.WriteLine("  ┌─ Categories ─────────────────────────────────");
foreach (var kv in a.CategoryCounts.OrderByDescending(x => x.Value).Take(10))
    Console.WriteLine($"  │  {kv.Key,-14} {kv.Value,6:N0} files   {FormatBytes(a.CategorySizes.GetValueOrDefault(kv.Key))}");
Console.WriteLine("  └─────────────────────────────────────────────");
Console.WriteLine();

// Top 10 extensions by count
Console.WriteLine("  ┌─ Top Extensions (by count) ──────────────────");
foreach (var kv in a.ExtensionCounts.OrderByDescending(x => x.Value).Take(10))
    Console.WriteLine($"  │  .{kv.Key,-13} {kv.Value,6:N0} files   {FormatBytes(a.ExtensionSizes.GetValueOrDefault(kv.Key))}");
Console.WriteLine("  └─────────────────────────────────────────────");
Console.WriteLine();

// Timestamps
Console.WriteLine("  ┌─ File Age ───────────────────────────────────");
Console.WriteLine($"  │  Oldest file  : {(a.OldestFile == DateTime.MaxValue ? "n/a" : a.OldestFile.ToString("yyyy-MM-dd"))}");
Console.WriteLine($"  │  Newest file  : {(a.NewestFile == DateTime.MinValue ? "n/a" : a.NewestFile.ToString("yyyy-MM-dd"))}");
Console.WriteLine("  └─────────────────────────────────────────────");
Console.WriteLine();

// Immediate subdirs
Console.WriteLine("  ┌─ Immediate Subdirectories (by size) ─────────");
foreach (var d in a.ImmediateSubDirs.OrderByDescending(x => x.Size).Take(10))
    Console.WriteLine($"  │  {FormatBytes(d.Size),10}   {d.Name}");
Console.WriteLine("  └─────────────────────────────────────────────");
Console.WriteLine();

// Flagged paths — hidden
if (a.HiddenPaths.Count > 0)
{
    Console.WriteLine($"  ┌─ Hidden files ({a.HiddenPaths.Count:N0} total) ─────────────────");
    foreach (var p in a.HiddenPaths.Take(10))
        Console.WriteLine($"  │  {p}");
    if (a.HiddenPaths.Count > 10)
        Console.WriteLine($"  │  … and {a.HiddenPaths.Count - 10} more");
    Console.WriteLine("  └─────────────────────────────────────────────");
    Console.WriteLine();
}

// Flagged paths — system
if (a.SystemPaths.Count > 0)
{
    Console.WriteLine($"  ┌─ System files ({a.SystemPaths.Count:N0} total) ─────────────────");
    foreach (var p in a.SystemPaths.Take(10))
        Console.WriteLine($"  │  {p}");
    if (a.SystemPaths.Count > 10)
        Console.WriteLine($"  │  … and {a.SystemPaths.Count - 10} more");
    Console.WriteLine("  └─────────────────────────────────────────────");
    Console.WriteLine();
}

// Skipped paths — permission / IO errors
if (a.SkippedPaths.Count > 0)
{
    Warn($"{a.SkippedPaths.Count} path(s) skipped (permission / IO errors):");
    foreach (var p in a.SkippedPaths.Take(10))
        Console.WriteLine($"    • {p}");
    if (a.SkippedPaths.Count > 10)
        Console.WriteLine($"    … and {a.SkippedPaths.Count - 10} more");
    Console.WriteLine();
}

if (tree.BasicInfo.SkippedPaths.Count > 0)
{
    Console.WriteLine($"[scanner] {tree.BasicInfo.SkippedPaths.Count} path(s) skipped (permission/IO errors):");
    foreach (var p in tree.BasicInfo.SkippedPaths.Take(10))
        Console.WriteLine($"  • {p}");
    if (tree.BasicInfo.SkippedPaths.Count > 10)
        Console.WriteLine($"  … and {tree.BasicInfo.SkippedPaths.Count - 10} more.");
}

// ── Save ──────────────────────────────────────────────────────────────────────

if (scanner is Directoryscanner ds)
{
    var saved = await ds.SaveAsync(tree, output);
    Console.WriteLine($"[scanner] FileTree written → {saved}");
}

// --- STEP 3: PRIORITIZE ---
IScoringEngine scoringEngine = new ScoringEngine();
// Use default weights for the first test
List<ScoredFileNode> scoredNodes;

try
{
    scoredNodes = await scoringEngine.ScoreAsync(report, null);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("\n[scoring] Scoring cancelled.");
    return 3;
}

// 1. Show the Top 10 "High Importance" Files with Breakdown
Console.WriteLine("\n[TOP 10 RANKED FILES - DETAILED BREAKDOWN]");
Console.WriteLine($"{"SCORE",-6} | {"FILE NAME",-25} | {"REC",-5} | {"TYP",-5} | {"SIZ",-5} | {"PEN",-5} | {"RAW",-7}");
Console.WriteLine(new string('-', 75));

var topFiles = scoredNodes
    .OrderByDescending(f => f.Score)
    .Take(10);

foreach (var scored in topFiles)
{
    var b = scored.ScoreBreakdown;
    
    // Aggregate penalties for a cleaner view
    double penalties = b.GetValueOrDefault("dup_pen", 0) + b.GetValueOrDefault("sys_pen", 0);
    
    string fileName = scored.Node.Name.Length > 25 
        ? scored.Node.Name[..22] + "..." 
        : scored.Node.Name;

    // Formatting the output into a diagnostic table
    Console.WriteLine($"{scored.Score,-6} | " +
                      $"{fileName,-25} | " +
                      $"{b.GetValueOrDefault("recency", 0),-5:F1} | " +
                      $"{b.GetValueOrDefault("type", 0),-5:F1} | " +
                      $"{b.GetValueOrDefault("size", 0),-5:F1} | " +
                      $"{penalties,-5:F1} | " +
                      $"{b.GetValueOrDefault("raw", 0),-7:F2}");
}

// 2. Show "The Junk" with why it failed
Console.WriteLine("\n[POTENTIAL JUNK (BOTTOM 3)]");
var bottomFiles = scoredNodes
    .OrderBy(f => f.Score)
    .Take(3);

foreach (var scored in bottomFiles)
{
    var b = scored.ScoreBreakdown;
    double raw = b.GetValueOrDefault("raw", 0);
    
    // Identify the "killing blow" for the score
    string reason = b.GetValueOrDefault("sys_pen", 0) > 0 ? "[System File]" :
        b.GetValueOrDefault("dup_pen", 0) > 0 ? "[Duplicate]" : 
        "[Old/Junk Type]";

    Console.WriteLine($"  - {scored.Node.Name,-30} | Score: {scored.Score,-3} | {reason}");
}

// 3. Stats Summary
Console.WriteLine("\n[ENGINE STATS]");
Console.WriteLine($"  Total Scored:   {scoredNodes.Count}");
Console.WriteLine($"  Average Score:  {scoredNodes.Average(f => f.Score):F1}");
    
Console.WriteLine("\n================================================================");
Console.WriteLine("Pipeline Check: [SCAN: OK] -> [ANALYZE: OK] -> [SCORE: OK]");

// --- Mocking a ProposedPlan for Editor Testing ---
var mockProposedPlan = new ProposedPlan
{
    Id = Guid.NewGuid(),
    GeneratedAt = DateTime.UtcNow,
    // Reuse your existing list of operations here
    Operations = new List<PlannedOperation>
    {
        // --- Images ---
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\1.png", DestinationPath = @"H:\SDMSTest\Images\1.png", Status = OpStatus.Confirmed, Reason = "Organizing Images" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\2.png", DestinationPath = @"H:\SDMSTest\Images\2.png", Status = OpStatus.Confirmed, Reason = "Organizing Images" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\3.png", DestinationPath = @"H:\SDMSTest\Images\3.png", Status = OpStatus.Confirmed, Reason = "Organizing Images" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\4.png", DestinationPath = @"H:\SDMSTest\Images\4.png", Status = OpStatus.Confirmed, Reason = "Organizing Images" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\5.png", DestinationPath = @"H:\SDMSTest\Images\5.png", Status = OpStatus.Confirmed, Reason = "Organizing Images" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\6.png", DestinationPath = @"H:\SDMSTest\Images\6.png", Status = OpStatus.Confirmed, Reason = "Organizing Images" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\7.png", DestinationPath = @"H:\SDMSTest\Images\7.png", Status = OpStatus.Confirmed, Reason = "Organizing Images" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\Screenshot 2026-04-27 220440.png", DestinationPath = @"H:\SDMSTest\Images\Screenshot 2026-04-27 220440.png", Status = OpStatus.Confirmed, Reason = "Organizing Images" },

        // --- Documents (Project Specific) ---
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\Anime Genre Classifier from Cover Art using CNNs.docx", DestinationPath = @"H:\SDMSTest\Documents\Anime Genre Classifier from Cover Art using CNNs\Anime Genre Classifier from Cover Art using CNNs.docx", Status = OpStatus.Confirmed, Reason = "Grouping Project Files" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\Anime Genre Classifier from Cover Art using CNNs.pdf", DestinationPath = @"H:\SDMSTest\Documents\Anime Genre Classifier from Cover Art using CNNs\Anime Genre Classifier from Cover Art using CNNs.pdf", Status = OpStatus.Confirmed, Reason = "Grouping Project Files" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\HCI Project Proposal.docx", DestinationPath = @"H:\SDMSTest\Documents\HCI Project Proposal\HCI Project Proposal.docx", Status = OpStatus.Confirmed, Reason = "Grouping Project Files" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\HCI Project Proposal.pdf", DestinationPath = @"H:\SDMSTest\Documents\HCI Project Proposal\HCI Project Proposal.pdf", Status = OpStatus.Confirmed, Reason = "Grouping Project Files" },

        // --- Documents (General) ---
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\L23-0662_SE_Activity.docx", DestinationPath = @"H:\SDMSTest\Documents\L23-0662_SE_Activity.docx", Status = OpStatus.Confirmed, Reason = "Sorting Documents" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\Personal Commitments.txt", DestinationPath = @"H:\SDMSTest\Documents\Personal Commitments.txt", Status = OpStatus.Confirmed, Reason = "Sorting Documents" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\SE Project Proposal - Copy.docx", DestinationPath = @"H:\SDMSTest\Documents\SE Project Proposal - Copy.docx", Status = OpStatus.Confirmed, Reason = "Sorting Documents" },

        // --- Databases, HTML, Sheets ---
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\AnimeReleaseDB.accdb", DestinationPath = @"H:\SDMSTest\Database\AnimeReleaseDB.accdb", Status = OpStatus.Confirmed, Reason = "Database Consolidation" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\Database1.accdb", DestinationPath = @"H:\SDMSTest\Database\Database1.accdb", Status = OpStatus.Confirmed, Reason = "Database Consolidation" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\calisthenics.html", DestinationPath = @"H:\SDMSTest\HTMLs\calisthenics.html", Status = OpStatus.Confirmed, Reason = "Web Sorting" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\VocalRange.xlsx", DestinationPath = @"H:\SDMSTest\Sheets\VocalRange.xlsx", Status = OpStatus.Confirmed, Reason = "Spreadsheet Sorting" },

        // --- Deletions ---
        new() { Id = Guid.NewGuid(), Type = OpType.Delete, SourcePath = @"H:\SDMSTest\Default.rdp", Status = OpStatus.Confirmed, Reason = "Cleanup" },
        new() { Id = Guid.NewGuid(), Type = OpType.Delete, SourcePath = @"H:\SDMSTest\github-recovery-codes.txt", Status = OpStatus.Confirmed, Reason = "Security Cleanup" },
        new() { Id = Guid.NewGuid(), Type = OpType.Delete, SourcePath = @"H:\SDMSTest\My Cheat TablesExceptionAutoSave_noname.ct", Status = OpStatus.Confirmed, Reason = "Temp File Cleanup" },
        new() { Id = Guid.NewGuid(), Type = OpType.Delete, SourcePath = @"H:\SDMSTest\teest.srt", Status = OpStatus.Confirmed, Reason = "Junk Cleanup" },
        new() { Id = Guid.NewGuid(), Type = OpType.Delete, SourcePath = @"H:\SDMSTest\what.CEA", Status = OpStatus.Confirmed, Reason = "Cleanup" }
    }
};

// 1. Load the editor with the AI's plan
var editor = new PlanEditor(mockProposedPlan);

Console.WriteLine("Testing Plan Editor edits...");

// --- Simulate a "Rescue" Edit ---
// User decides NOT to delete the recovery codes
var recoveryCodesOp = editor.CurrentOperations.First(o => o.SourcePath.Contains("github-recovery-codes.txt"));
editor.SetStatus(recoveryCodesOp.Id, OpStatus.Skipped); 
Console.WriteLine("RESCUED: github-recovery-codes.txt (Status set to Skipped)");

// --- Simulate a "Destination Change" Edit ---
// User wants 1.png in 'Photos' instead of 'Images'
var firstImage = editor.CurrentOperations.First(o => o.SourcePath.Contains("1.png"));
// Use your Clone method, then manually update the property
var updatedImageOp = firstImage.Clone();
updatedImageOp.DestinationPath = @"H:\SDMSTest\Photos\1.png";

editor.UpdateOperation(firstImage.Id, updatedImageOp);
Console.WriteLine(@"CHANGED: 1.png destination updated to \Photos\");

// --- Testing Undo ---
if (editor.CanUndo)
{
    editor.Undo();
    Console.WriteLine(@"UNDO: Reverted 1.png destination back to \Images\");
}

// --- PHASE 2: THE HAND-OFF ---
var finalCheck = editor.ValidatePlan();
if (finalCheck.IsValid)
{
    // The Editor produces the 'final' plan here
    FinalizedPlan final = editor.Finalize(mockProposedPlan);

    // --- PHASE 3: PHYSICAL EXECUTION (The logic you provided) ---
    // 1. Initialize the Engine
    IExecutionEngine executionEngine = new ExecutionEngine();

    // 2. Run Preflight (Checks for disk space, write permissions, etc.)
    var validation = await executionEngine.PreflightAsync(final);

    if (validation.IsValid)
    {
        // 3. Commit the changes to the H: drive
        var exoptions = new ExecutionOptions 
        { 
            UseStagingForDeletes = true, // Moves to Recycle Bin instead of hard delete
            DryRun = false               // Set to true if you just want to test logs
        };
        
        var log = await executionEngine.ExecuteAsync(final, exoptions);
        
        Console.WriteLine($"[Execution] Completed with {log.SuccessCount} successes.");
    }
    else 
    {
        Console.WriteLine("Preflight failed! Check if paths are still valid.");
    }
}

return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

static string TruncatePath(string path, int max) =>
    path.Length <= max ? path : "…" + path[^(max - 1)..];

static void PrintHelp() => Console.WriteLine("""
    SDMS — Directory Scanner  (Module 1)
    =====================================
    Usage: scanner <root-path> [options]

    Arguments:
      <root-path>          Directory to scan (required)

    Options:
      --output  <path>     Output file path       (default: filetree.json)
      --format  <fmt>      json | msgpack          (default: json)
      --compact            Compact JSON, no indent
      --hidden             Include hidden files/dirs
      --system             Include system files/dirs
      --symlinks           Follow symbolic links (warning: may loop)
      --depth   <n>        Max recursion depth    (default: unlimited)
      --maxsize <bytes>    Skip files larger than this
      -h, --help           Show this help

    Examples:
      scanner /home/user --depth 5 --hidden --output tree.json
      scanner C:\Users   --format msgpack --output tree.msgpack
      scanner /srv/data  --maxsize 104857600   # skip files > 100 MB
    """);

string FormatBytes(long bytes) => bytes switch
{
    < 1_024               => $"{bytes} B",
    < 1_048_576           => $"{bytes / 1024.0:F1} KB",
    < 1_073_741_824       => $"{bytes / 1_048_576.0:F1} MB",
    _                     => $"{bytes / 1_073_741_824.0:F2} GB",
};
void Warn(string msg) => WriteColored("  [WARN] ", ConsoleColor.Yellow, msg);
void WriteColored(string prefix, ConsoleColor color, string msg)
{
    Console.ForegroundColor = color;
    Console.Write(prefix);
    Console.ResetColor();
    Console.WriteLine(msg);
}

void PrintDistributions(AnalysisReport report)
{
    Console.WriteLine("\n==================================================");
    Console.WriteLine($"{"EXTENSION",-15} | {"COUNT",-8} | {"TOTAL SIZE",-15}");
    Console.WriteLine("--------------------------------------------------");

    // Sort by count descending to see the most frequent types first
    var sortedExts = report.FileTypeDistribution
        .OrderByDescending(x => x.Value)
        .ToList();

    foreach (var entry in sortedExts)
    {
        string ext = string.IsNullOrEmpty(entry.Key) ? "(no ext)" : entry.Key;
        int count = entry.Value;
        long sizeInBytes = report.FileTypeSizeMap.GetValueOrDefault(entry.Key, 0L);
            
        // Format size for readability (e.g., 1.2 MB)
        string readableSize = FormatBytes(sizeInBytes);

        Console.WriteLine($"{ext,-15} | {count,-8} | {readableSize,-15}");
    }

    Console.WriteLine("==================================================");
    Console.WriteLine($"Total Size: {FormatBytes(report.SourceTree.BasicInfo.TotalSizeBytes)}");
    Console.WriteLine($"System Files Hidden: {report.SystemFiles.Count}");
    Console.WriteLine($"Suggested Labels: {string.Join(", ", report.RequiredLabels)}");
}