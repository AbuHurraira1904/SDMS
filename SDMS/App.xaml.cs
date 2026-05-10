using System.IO;
using System.Windows;
using SDMS.Application;
using SDMS.Domain.Scanner;
using SDMS.Infrastructure.Scanner;
using SDMS.Infrastructure.Serialization;
using SDMS.Domain.Models;

namespace SDMS;

public partial class App : System.Windows.Application
{
    // OnStartup fires instead of showing a window.
    // When you are ready to show the UI, remove this override
    // and set StartupUri in App.xaml back to MainWindow.xaml.
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Hide the WPF dispatcher — we are running console-style output.
        // Allocate a console window so Console.WriteLine is visible in Rider.
        AllocConsole();

        await RunScannerTest(e.Args);

        // Keep the console open until the user presses a key.
        Console.WriteLine();
        Console.WriteLine("  Press any key to exit …");
        Console.ReadKey();

        Shutdown();
    }

    // ── Test runner ───────────────────────────────────────────────────────────

    private static async Task RunScannerTest(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════╗");
        Console.WriteLine("║     SDMS — Module 1 Scanner  Console Test    ║");
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.WriteLine();

        // ── Pick root path ────────────────────────────────────────────────────
        // Pass a path via Rider's run config (Program arguments) or fall back
        // to the current working directory.
        string rootPath = args[0];
            
        if (!Directory.Exists(rootPath))
        {
            Fail($"Directory not found: '{rootPath}'");
            return;
        }

        Console.WriteLine($"  Root path : {rootPath}");
        Console.WriteLine();

        // ── Scan options ──────────────────────────────────────────────────────
        var options = new ScanOptions
        {
            FollowSymlinks      = false,
            IncludeHidden       = false,
            IncludeSystemFiles  = false,
            MaxDepth            = 5,
            MaxFileSizeBytes    = null,
            ExcludedDirectoryNames = ["node_modules", ".git", ".vs", "bin", "obj"],
            ExcludedExtensions  = [],
        };

        PrintOptions(options);

        // ── Wire dependencies ─────────────────────────────────────────────────
        IMetaDataExtractor_Interface meta   = new MetaDataExtractor();
        ISysLinkGuard_Interface      guard  = new SysLinkGuard();
        IMountDectector_Interface    mounts = new MountDetector();
        ITreeWalk_Interface          walker = new TreeWalker(meta, guard, mounts);
        ITreeSerializer_Interface    serial = new JSONTreeSerializer(compact: false);
        IDirectoryScanner            scanner = new Directoryscanner(walker, serial);

        // ── Progress display ──────────────────────────────────────────────────
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var progress = new Progress<(int FilesScanned, string CurrentPath)>(r =>
        {
            string path = r.CurrentPath.Length > 55
                ? "…" + r.CurrentPath[^54..] : r.CurrentPath;
            Console.Write($"\r  Scanning … {r.FilesScanned,6:N0} files   {path,-55}");
        });

        // ── Run ───────────────────────────────────────────────────────────────
        FileTree tree;
        try
        {
            tree = await scanner.ScanAsync(rootPath, options, progress);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Fail($"Scan failed: {ex.Message}");
            return;
        }

        sw.Stop();
        Console.WriteLine();
        Console.WriteLine();

        // ── Summary ───────────────────────────────────────────────────────────
        Pass("Scan completed successfully");
        Console.WriteLine();
        Console.WriteLine("  ┌─ Summary ───────────────────────────────────┐");
        Console.WriteLine($"  │  Files          : {tree.TotalFiles,10:N0}               │");
        Console.WriteLine($"  │  Directories    : {tree.TotalDirectories,10:N0}               │");
        Console.WriteLine($"  │  Total size     : {FormatBytes(tree.TotalSizeBytes),10}               │");
        Console.WriteLine($"  │  Ignored files  : {tree.TotalIgnoredFiles,10:N0}               │");
        Console.WriteLine($"  │  Skipped paths  : {tree.SkippedPaths.Count,10:N0}               │");
        Console.WriteLine($"  │  Elapsed        : {sw.Elapsed.TotalSeconds,9:F2}s               │");
        Console.WriteLine("  └─────────────────────────────────────────────┘");
        Console.WriteLine();

        // ── Skipped paths ─────────────────────────────────────────────────────
        if (tree.SkippedPaths.Count > 0)
        {
            Warn($"{tree.SkippedPaths.Count} path(s) skipped (permission / IO errors):");
            foreach (var p in tree.SkippedPaths.Take(5))
                Console.WriteLine($"    • {p}");
            if (tree.SkippedPaths.Count > 5)
                Console.WriteLine($"    … and {tree.SkippedPaths.Count - 5} more.");
            Console.WriteLine();
        }

        // ── Top-level tree ────────────────────────────────────────────────────
        Console.WriteLine("  ┌─ Top-level tree (depth 2) ──────────────────");
        PrintTree(tree.Root, prefix: "  │  ", maxDepth: 2, depth: 0);
        Console.WriteLine("  └─────────────────────────────────────────────");
        Console.WriteLine();

        // ── MIME breakdown ────────────────────────────────────────────────────
        var mimeGroups = FlattenFiles(tree.Root)
            .GroupBy(f => f.MimeType)
            .OrderByDescending(g => g.Count())
            .ToList();

        Console.WriteLine("  ┌─ MIME breakdown ─────────────────────────────");
        foreach (var g in mimeGroups)
        {
            string bar = new string('█', Math.Min(g.Count(), 25));
            Console.WriteLine($"  │  {g.Key,-12}  {g.Count(),5:N0}  {bar}");
        }
        Console.WriteLine("  └─────────────────────────────────────────────");
        Console.WriteLine();

        // ── Top 5 largest files ───────────────────────────────────────────────
        var largest = FlattenFiles(tree.Root)
            .OrderByDescending(f => f.SizeBytes)
            .Take(5)
            .ToList();

        Console.WriteLine("  ┌─ Top 5 largest files ────────────────────────");
        foreach (var f in largest)
        {
            string name = f.Name.Length > 38 ? f.Name[..37] + "…" : f.Name;
            Console.WriteLine($"  │  {FormatBytes(f.SizeBytes),10}   {name}");
        }
        Console.WriteLine("  └─────────────────────────────────────────────");
        Console.WriteLine();

        // ── JSON save ─────────────────────────────────────────────────────────
        string outputPath = Path.Combine(Path.GetTempPath(), "sdms_test_output.json");
        try
        {
            if (scanner is Directoryscanner ds)
                await ds.SaveAsync(tree, outputPath);

            var info = new FileInfo(outputPath);
            if (info.Exists && info.Length > 0)
                Pass($"JSON saved → {outputPath}  ({FormatBytes(info.Length)})");
            else
                Fail("JSON file was created but is empty.");
        }
        catch (Exception ex) { Fail($"Serialisation failed: {ex.Message}"); }

        Console.WriteLine();

        // ── Round-trip ────────────────────────────────────────────────────────
        try
        {
            var loaded = await new JSONTreeSerializer().DeserializeAsync(outputPath);
            if (loaded.ScanRootPath == tree.ScanRootPath && loaded.TotalFiles == tree.TotalFiles)
                Pass("JSON round-trip OK — deserialized tree matches original.");
            else
                Fail($"Round-trip mismatch: files expected={tree.TotalFiles} got={loaded.TotalFiles}");
        }
        catch (Exception ex) { Fail($"Deserialisation failed: {ex.Message}"); }

        Console.WriteLine();
        Console.WriteLine("  All checks done.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void Pass(string msg) => WriteColored("  [PASS] ", ConsoleColor.Green,  msg);
    private static void Fail(string msg) => WriteColored("  [FAIL] ", ConsoleColor.Red,    msg);
    private static void Warn(string msg) => WriteColored("  [WARN] ", ConsoleColor.Yellow, msg);

    private static void WriteColored(string prefix, ConsoleColor color, string msg)
    {
        Console.ForegroundColor = color;
        Console.Write(prefix);
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    private static void PrintOptions(ScanOptions o)
    {
        Console.WriteLine("  ┌─ Scan options ───────────────────────────────");
        Console.WriteLine($"  │  Follow symlinks  : {o.FollowSymlinks}");
        Console.WriteLine($"  │  Include hidden   : {o.IncludeHidden}");
        Console.WriteLine($"  │  Include system   : {o.IncludeSystemFiles}");
        Console.WriteLine($"  │  Max depth        : {o.MaxDepth?.ToString() ?? "unlimited"}");
        Console.WriteLine($"  │  Excluded dirs    : {string.Join(", ", o.ExcludedDirectoryNames)}");
        Console.WriteLine("  └─────────────────────────────────────────────");
        Console.WriteLine();
    }

    private static void PrintTree(FileNode node, string prefix, int maxDepth, int depth)
    {
        if (depth > maxDepth) return;
        var children = node.Children.ToList();
        for (int i = 0; i < children.Count; i++)
        {
            var child   = children[i];
            bool isLast = i == children.Count - 1;
            string conn = isLast ? "└── " : "├── ";
            string icon = child.IsDirectory ? "📁" : "📄";
            string size = child.IsDirectory ? "" : $"  ({FormatBytes(child.SizeBytes)})";
            Console.WriteLine($"{prefix}{conn}{icon} {child.Name}{size}");

            if (child.IsDirectory && depth < maxDepth)
                PrintTree(child, prefix + (isLast ? "    " : "│   "), maxDepth, depth + 1);
        }
    }

    private static IEnumerable<FileNode> FlattenFiles(FileNode node)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsDirectory) yield return child;
            else foreach (var f in FlattenFiles(child)) yield return f;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1_024               => $"{bytes} B",
        < 1_048_576           => $"{bytes / 1024.0:F1} KB",
        < 1_073_741_824       => $"{bytes / 1_048_576.0:F1} MB",
        _                     => $"{bytes / 1_073_741_824.0:F2} GB",
    };

    // ── AllocConsole — opens a console window in a WPF process ───────────────
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
}