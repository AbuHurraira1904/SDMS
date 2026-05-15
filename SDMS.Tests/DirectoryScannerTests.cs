// ============================================================
// DirectoryScannerTests.cs  →  SDMS.Tests/Scanner/
// Tests for SDMS.Application.Directoryscanner
//
// Directoryscanner has zero direct OS calls — it delegates to
// ITreeWalk_Interface and ITreeSerializer_Interface. We test it
// with lightweight fakes; no Moq required.
//
// What we're testing:
//   1.  ScanAsync on a non-existent path throws DirectoryNotFoundException
//   2.  ScanAsync returns a FileTree with correct ScanRootPath
//   3.  ScanAsync returns a FileTree with ScannedAt populated
//   4.  ScanAsync returns the root FileNode produced by the walker
//   5.  ScanAsync copies BasicInfo metrics from the walker's Analysis
//   6.  Progress is forwarded from the walker to the caller
//   7.  Cancellation token is passed through to the walker
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SDMS.Application;
using SDMS.Domain.Models;
using SDMS.Domain.Scanner;
using SDMS.Infrastructure.Scanner;
using SDMS.Infrastructure.Serialization;
using SDMS.Tests.Helpers;
using Xunit;

namespace SDMS.Tests.Scanner;

// ── Fakes ────────────────────────────────────────────────────────────────────

/// <summary>
/// Fake walker that returns a pre-built FileNode root.
/// Lets us decouple DirectoryScanner tests from real filesystem access.
/// </summary>
internal sealed class FakeWalker : ITreeWalk_Interface
{
    private readonly FileNode _root;
    private readonly FolderAnalysisMetrics _analysis;
    private readonly bool _throwOnCancel;

    public FolderAnalysisMetrics Analysis => _analysis;

    public FakeWalker(
        FileNode? root            = null,
        FolderAnalysisMetrics? analysis = null,
        bool throwOnCancel        = false)
    {
        _root          = root     ?? Builders.Dir("Root");
        _analysis      = analysis ?? Builders.MetricsFromRoot(_root);
        _throwOnCancel = throwOnCancel;
    }

    public Task<FileNode> WalkAsync(
        string rootPath,
        ScanOptions options,
        IProgress<(int FilesScanned, string CurrentPath)>? progress = null,
        CancellationToken ct = default)
    {
        if (_throwOnCancel)
            ct.ThrowIfCancellationRequested();

        // Emit one progress event so callers can verify forwarding
        progress?.Report((1, rootPath));

        return Task.FromResult(_root);
    }
}

/// <summary>
/// Fake serializer that captures what it receives (no actual I/O).
/// </summary>
internal sealed class FakeSerializer : ITreeSerializer_Interface
{
    public FileTree? LastSerialized { get; private set; }

    public Task<string> SerializeAsync(FileTree tree, string outputPath, CancellationToken ct = default)
    {
        LastSerialized = tree;
        return Task.FromResult(outputPath);
    }

    public Task<FileTree> DeserializeAsync(string inputPath, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

// ── Tests ────────────────────────────────────────────────────────────────────

public sealed class DirectoryScannerTests : IDisposable
{
    // A real temp directory is needed for the "path must exist" check.
    private readonly string _tmpDir;

    public DirectoryScannerTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"SDMS_ScanTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, recursive: true);
    }

    private Directoryscanner Make(
        ITreeWalk_Interface? walker         = null,
        ITreeSerializer_Interface? serializer = null)
    {
        return new Directoryscanner(
            walker     ?? new FakeWalker(),
            serializer ?? new FakeSerializer());
    }

    // ── 1. Non-existent path throws ───────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_NonExistentPath_ThrowsDirectoryNotFoundException()
    {
        var scanner = Make();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => scanner.ScanAsync("C:\\Does\\Not\\Exist", Builders.DefaultOptions()));
    }

    // ── 2. ScanRootPath is set correctly ──────────────────────────────────────

    [Fact]
    public async Task ScanAsync_ReturnsTree_WithCorrectScanRootPath()
    {
        var scanner = Make();
        var tree    = await scanner.ScanAsync(_tmpDir, Builders.DefaultOptions());

        Assert.Equal(Path.GetFullPath(_tmpDir), tree.ScanRootPath);
    }

    // ── 3. ScannedAt is populated ─────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_ReturnsTree_WithScannedAtPopulated()
    {
        var before  = DateTime.UtcNow.AddSeconds(-1);
        var scanner = Make();
        var tree    = await scanner.ScanAsync(_tmpDir, Builders.DefaultOptions());
        var after   = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(tree.ScannedAt, before, after);
    }

    // ── 4. Root node comes from walker ────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_Root_IsNodeReturnedByWalker()
    {
        var expectedRoot = Builders.Dir("MyRoot");
        var walker       = new FakeWalker(root: expectedRoot);
        var scanner      = Make(walker: walker);

        var tree = await scanner.ScanAsync(_tmpDir, Builders.DefaultOptions());

        Assert.Same(expectedRoot, tree.Root);
    }

    // ── 5. BasicInfo is a deep copy of walker's Analysis ──────────────────────

    [Fact]
    public async Task ScanAsync_BasicInfo_CopiesWalkerAnalysisMetrics()
    {
        var metrics = new FolderAnalysisMetrics();
        metrics.TotalFiles = 42;
        metrics.TotalSizeBytes = 999_999;
        metrics.ExtensionCounts["pdf"] = 10;

        var walker  = new FakeWalker(analysis: metrics);
        var scanner = Make(walker: walker);

        var tree = await scanner.ScanAsync(_tmpDir, Builders.DefaultOptions());

        Assert.Equal(42,      tree.BasicInfo.TotalFiles);
        Assert.Equal(999_999, tree.BasicInfo.TotalSizeBytes);
        Assert.Equal(10,      tree.BasicInfo.ExtensionCounts["pdf"]);
    }

    // ── 6. Progress is forwarded ──────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_ForwardsProgressFromWalker()
    {
        var scanner  = Make();
        var reported = new List<(int FilesScanned, string CurrentPath)>();
        var progress = new Progress<(int FilesScanned, string CurrentPath)>(r => reported.Add(r));

        await scanner.ScanAsync(_tmpDir, Builders.DefaultOptions(), progress);

        // The FakeWalker emits one progress report
        Assert.NotEmpty(reported);
    }

    // ── 7. Cancellation is forwarded ──────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_WithCancelledToken_ThrowsOperationCancelledException()
    {
        var walker  = new FakeWalker(throwOnCancel: true);
        var scanner = Make(walker: walker);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.ScanAsync(_tmpDir, Builders.DefaultOptions(), ct: cts.Token));
    }
}
