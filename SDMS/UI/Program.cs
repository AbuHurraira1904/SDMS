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
    $"[scanner] Done — {tree.TotalFiles:N0} files, " +
    $"{tree.TotalDirectories:N0} dirs, " +
    $"{tree.TotalSizeBytes:N0} bytes, " +
    $"{tree.TotalIgnoredFiles:N0} ignored " +
    $"in {elapsed.TotalSeconds:F2}s");

if (tree.SkippedPaths.Count > 0)
{
    Console.WriteLine($"[scanner] {tree.SkippedPaths.Count} path(s) skipped (permission/IO errors):");
    foreach (var p in tree.SkippedPaths.Take(10))
        Console.WriteLine($"  • {p}");
    if (tree.SkippedPaths.Count > 10)
        Console.WriteLine($"  … and {tree.SkippedPaths.Count - 10} more.");
}

// ── Save ──────────────────────────────────────────────────────────────────────

if (scanner is Directoryscanner ds)
{
    var saved = await ds.SaveAsync(tree, output);
    Console.WriteLine($"[scanner] FileTree written → {saved}");
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
