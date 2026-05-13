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
using SDMS.Infrastructure.Scanner;
using SDMS.Infrastructure.Serialization;
using SDMS.Domain.Models;
using SDMS.Infrastructure.Execution;
using SDMS.Domain.Execution;
using System.Linq;


// ── Encoding (MUST be first — before any Console I/O) ────────────────────────
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding  = System.Text.Encoding.UTF8;

// ── Reinitialize Console.In from the real keyboard device ────────────────────
// After async/await the runtime's stdin StreamReader can reach an internal EOF
// state and return "" from ReadLine() forever. Opening CONIN$ (Windows) or
// /dev/tty (Linux/macOS) directly bypasses that poisoned stream entirely.
// We do this ONCE at startup so every ReadLine() in the program works.
try
{
    string conDevice = OperatingSystem.IsWindows() ? "CONIN$" : "/dev/tty";
    var    conStream = new FileStream(conDevice, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    Console.SetIn(new StreamReader(conStream, System.Text.Encoding.UTF8));
}
catch
{
    // No TTY (e.g. CI pipeline) — Console.ReadLine() will still be attempted,
    // prompts will just have no effect which is acceptable in that context.
}

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
int?   maxDepth       = 5;   // default: 5 levels from the argument path (D:\ = 0)
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
    FollowSymlinks     = symlinks,
    IncludeHidden      = hidden,
    IncludeSystemFiles = system,
    MaxDepth           = maxDepth,
    MaxFileSizeBytes   = maxFileBytes,
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
    $"[scanner] Done — {tree.BasicInfo.TotalFiles:N0} files, " +
    $"{tree.BasicInfo.TotalDirectories:N0} dirs, " +
    $"{tree.BasicInfo.TotalSizeBytes:N0} bytes, " +
    $"{tree.BasicInfo.TotalIgnoredFiles:N0} ignored " +
    $"in {elapsed.TotalSeconds:F2}s");

// ── Analysis ──────────────────────────────────────────────────────────────────
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

// ── Save filetree.json ────────────────────────────────────────────────────────

string savedPath = output;
if (scanner is Directoryscanner ds)
{
    savedPath = await ds.SaveAsync(tree, output);
    Console.WriteLine($"[scanner] FileTree written → {savedPath}");
}

// =====================================================
// MODULE 2 — MOVE SUGGESTION SUMMARY
// Walks the FileTree already in memory — no disk I/O,
// no JSON re-parse, no second directory walk.
// =====================================================

Console.WriteLine();
Console.WriteLine("==================================================");
Console.WriteLine("📌 SMART FILE MOVE RECOMMENDATION SUMMARY");
Console.WriteLine("==================================================");

string depthLabel = maxDepth.HasValue ? $"(max depth: {maxDepth})" : "(unlimited depth)";

// AnalyzeFromTree walks the in-memory FileTree object directly.
var recommendations = MoveSuggestionService.AnalyzeFromTree(tree, maxDepth);

var recommended = recommendations
    .Where(x => x.Probability >= 50)
    .OrderByDescending(x => x.Probability)
    .ToList();

var highRecommended = recommended
    .Where(x => x.Probability >= 80)
    .ToList();

Console.WriteLine($"📂 Total Files Analyzed {depthLabel}: {recommendations.Count}");
Console.WriteLine($"✅ Files Recommended to Move (>=50%): {recommended.Count}");
Console.WriteLine($"🔥 Highly Recommended (>=80%):        {highRecommended.Count}");
Console.WriteLine("==================================================");
Console.WriteLine();

// ── Show highly recommended files ────────────────────────────────────────────
Console.WriteLine("🔥 Highly Recommended Files (>=80%):");
Console.WriteLine("--------------------------------------------------");

if (highRecommended.Count == 0)
{
    Console.WriteLine("   None found.");
}
else
{
    foreach (var item in highRecommended)
    {
        string dest = string.IsNullOrEmpty(item.RecommendedFullPath)
            ? $"📁 [NEW] {item.RecommendedFolder}"
            : $"📁 {item.RecommendedFullPath}";
        Console.WriteLine(
            $"   📄 {item.FileName,-35} [in: {item.CurrentFolder,-20}]" +
            $"  →  {dest} ({item.Probability}%)");
    }
}

Console.WriteLine("--------------------------------------------------");
Console.WriteLine();

// ── Prompt for full list ──────────────────────────────────────────────────────
Console.Out.Flush();
Console.Write("Do you want to view ALL recommended files (>=50%)? (y/n): ");

string input = Console.ReadLine()?.Trim().ToLowerInvariant() ?? string.Empty;
Console.WriteLine();

if (input == "y")
{
    if (recommended.Count == 0)
    {
        Console.WriteLine("   No files meet the >=50% threshold.");
    }
    else
    {
        Console.WriteLine("📌 All Recommended Files (>=50%):");
        Console.WriteLine("--------------------------------------------------");

        foreach (var item in recommended)
        {
            string dest = string.IsNullOrEmpty(item.RecommendedFullPath)
                ? $"📁 [NEW] {item.RecommendedFolder}"
                : $"📁 {item.RecommendedFullPath}";
            Console.WriteLine(
                $"   📄 {item.FileName,-35} [in: {item.CurrentFolder,-20}]" +
                $"  →  {dest} ({item.Probability}%)");
        }

        Console.WriteLine("--------------------------------------------------");
    }
}

Console.WriteLine();

// =====================================================
// MODULE 3 — EXECUTION ENGINE
// Mocked FinalizedPlan — replace with real plan later.
// =====================================================

Console.WriteLine("==================================================");
Console.WriteLine("⚙️  EXECUTION ENGINE");
Console.WriteLine("==================================================");

// --- Mocking a FinalizedPlan for testing ---
var plan = new FinalizedPlan
{
    Id           = Guid.NewGuid(),
    SourcePlanId = Guid.NewGuid(),
    FinalizedAt  = DateTime.UtcNow,
    Operations   = new List<PlannedOperation>
    {
        // --- Images ---
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\1.png",                                   DestinationPath = @"H:\SDMSTest\Images\1.png",                                                                                         Status = OpStatus.Confirmed, Reason = "Organizing Images"       },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\2.png",                                   DestinationPath = @"H:\SDMSTest\Images\2.png",                                                                                         Status = OpStatus.Confirmed, Reason = "Organizing Images"       },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\3.png",                                   DestinationPath = @"H:\SDMSTest\Images\3.png",                                                                                         Status = OpStatus.Confirmed, Reason = "Organizing Images"       },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\4.png",                                   DestinationPath = @"H:\SDMSTest\Images\4.png",                                                                                         Status = OpStatus.Confirmed, Reason = "Organizing Images"       },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\5.png",                                   DestinationPath = @"H:\SDMSTest\Images\5.png",                                                                                         Status = OpStatus.Confirmed, Reason = "Organizing Images"       },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\6.png",                                   DestinationPath = @"H:\SDMSTest\Images\6.png",                                                                                         Status = OpStatus.Confirmed, Reason = "Organizing Images"       },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\7.png",                                   DestinationPath = @"H:\SDMSTest\Images\7.png",                                                                                         Status = OpStatus.Confirmed, Reason = "Organizing Images"       },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\Screenshot 2026-04-27 220440.png",        DestinationPath = @"H:\SDMSTest\Images\Screenshot 2026-04-27 220440.png",                                                              Status = OpStatus.Confirmed, Reason = "Organizing Images"       },

        // --- Documents (Project Specific) ---
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\Anime Genre Classifier from Cover Art using CNNs.docx", DestinationPath = @"H:\SDMSTest\Documents\Anime Genre Classifier from Cover Art using CNNs\Anime Genre Classifier from Cover Art using CNNs.docx", Status = OpStatus.Confirmed, Reason = "Grouping Project Files" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\Anime Genre Classifier from Cover Art using CNNs.pdf",  DestinationPath = @"H:\SDMSTest\Documents\Anime Genre Classifier from Cover Art using CNNs\Anime Genre Classifier from Cover Art using CNNs.pdf",  Status = OpStatus.Confirmed, Reason = "Grouping Project Files" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\HCI Project Proposal.docx",               DestinationPath = @"H:\SDMSTest\Documents\HCI Project Proposal\HCI Project Proposal.docx",                                             Status = OpStatus.Confirmed, Reason = "Grouping Project Files" },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\HCI Project Proposal.pdf",                DestinationPath = @"H:\SDMSTest\Documents\HCI Project Proposal\HCI Project Proposal.pdf",                                              Status = OpStatus.Confirmed, Reason = "Grouping Project Files" },

        // --- Documents (General) ---
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\L23-0662_SE_Activity.docx",               DestinationPath = @"H:\SDMSTest\Documents\L23-0662_SE_Activity.docx",                                                                  Status = OpStatus.Confirmed, Reason = "Sorting Documents"       },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\Personal Commitments.txt",                DestinationPath = @"H:\SDMSTest\Documents\Personal Commitments.txt",                                                                    Status = OpStatus.Confirmed, Reason = "Sorting Documents"       },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\SE Project Proposal - Copy.docx",        DestinationPath = @"H:\SDMSTest\Documents\SE Project Proposal - Copy.docx",                                                             Status = OpStatus.Confirmed, Reason = "Sorting Documents"       },

        // --- Databases, HTML, Sheets ---
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\AnimeReleaseDB.accdb",                    DestinationPath = @"H:\SDMSTest\Database\AnimeReleaseDB.accdb",                                                                         Status = OpStatus.Confirmed, Reason = "Database Consolidation"  },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\Database1.accdb",                         DestinationPath = @"H:\SDMSTest\Database\Database1.accdb",                                                                              Status = OpStatus.Confirmed, Reason = "Database Consolidation"  },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\calisthenics.html",                       DestinationPath = @"H:\SDMSTest\HTMLs\calisthenics.html",                                                                               Status = OpStatus.Confirmed, Reason = "Web Sorting"             },
        new() { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = @"H:\SDMSTest\VocalRange.xlsx",                         DestinationPath = @"H:\SDMSTest\Sheets\VocalRange.xlsx",                                                                                Status = OpStatus.Confirmed, Reason = "Spreadsheet Sorting"     },

        // --- Deletions ---
        new() { Id = Guid.NewGuid(), Type = OpType.Delete, SourcePath = @"H:\SDMSTest\Default.rdp",                                                                                                                                                                    Status = OpStatus.Confirmed, Reason = "Cleanup"                 },
        new() { Id = Guid.NewGuid(), Type = OpType.Delete, SourcePath = @"H:\SDMSTest\github-recovery-codes.txt",                                                                                                                                                      Status = OpStatus.Confirmed, Reason = "Security Cleanup"        },
        new() { Id = Guid.NewGuid(), Type = OpType.Delete, SourcePath = @"H:\SDMSTest\My Cheat TablesExceptionAutoSave_noname.ct",                                                                                                                                     Status = OpStatus.Confirmed, Reason = "Temp File Cleanup"       },
        new() { Id = Guid.NewGuid(), Type = OpType.Delete, SourcePath = @"H:\SDMSTest\teest.srt",                                                                                                                                                                      Status = OpStatus.Confirmed, Reason = "Junk Cleanup"            },
        new() { Id = Guid.NewGuid(), Type = OpType.Delete, SourcePath = @"H:\SDMSTest\what.CEA",                                                                                                                                                                       Status = OpStatus.Confirmed, Reason = "Cleanup"                 }
    }
};

// 1. Initialize the Engine
IExecutionEngine executionEngine = new ExecutionEngine();

// 2. Run Preflight before committing
var validation = await executionEngine.PreflightAsync(plan);

if (validation.IsValid)
{
    // 3. Execute with your specific options
    var exoptions = new ExecutionOptions
    {
        UseStagingForDeletes = true,
        DryRun               = false
    };

    var log = await executionEngine.ExecuteAsync(plan, exoptions);

    Console.WriteLine($"[Execution] Completed with {log.SuccessCount} successes.");
}

Console.WriteLine();
Console.WriteLine("✅ Done.");

return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

static string TruncatePath(string path, int max) =>
    path.Length <= max ? path : "…" + path[^(max - 1)..];

static void PrintHelp() => Console.WriteLine("""
    SDMS — Directory Scanner + Move Recommender
    ============================================
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
      --depth   <n>        Max recursion depth from <root-path>
                             0 = root files only
                             1 = root + 1 level of sub-folders
                             (default: 5)
      --maxsize <bytes>    Skip files larger than this
      -h, --help           Show this help

    Examples:
      scanner D:\          --depth 3 --output tree.json
      scanner /home/user   --hidden --depth 5
      scanner C:\Users     --format msgpack --output tree.msgpack
    """);

static string FormatBytes(long bytes) => bytes switch
{
    < 1_024         => $"{bytes} B",
    < 1_048_576     => $"{bytes / 1024.0:F1} KB",
    < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
    _               => $"{bytes / 1_073_741_824.0:F2} GB",
};

void Warn(string msg) => WriteColored("  [WARN] ", ConsoleColor.Yellow, msg);

void WriteColored(string prefix, ConsoleColor color, string msg)
{
    Console.ForegroundColor = color;
    Console.Write(prefix);
    Console.ResetColor();
    Console.WriteLine(msg);
}