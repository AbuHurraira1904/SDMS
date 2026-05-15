// ============================================================
// AnalysisEngineTests.cs  →  SDMS.Tests/Analysis/
// Tests for SDMS.Infrastructure.Analysis.AnalysisEngine
//
// What we're testing:
//   1. Output shape — all report fields are populated
//   2. FileTypeDistribution mirrors the tree's ExtensionCounts
//   3. SystemFiles/IgnoredFiles are populated via flag traversal
//   4. RequiredLabels heuristic (only extensions with count > 2)
//   5. Empty tree produces a valid but empty report
//   6. Deeply nested files are included (recursive traversal)
//   7. Cancellation is respected mid-analysis
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDMS.Domain.Models;
using SDMS.Infrastructure.Analysis;
using SDMS.Tests.Helpers;
using Xunit;

namespace SDMS.Tests.Analysis;

public sealed class AnalysisEngineTests
{
    private readonly AnalysisEngine _sut = new();

    // ── 1. Output shape ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ReturnsReport_WithSourceTreeSet()
    {
        var tree   = Builders.Tree(Builders.Dir());
        var report = await _sut.AnalyzeAsync(tree);

        Assert.Same(tree, report.SourceTree);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsReport_WithAnalyzedAtPopulated()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var report = await _sut.AnalyzeAsync(Builders.Tree(Builders.Dir()));
        var after  = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(report.AnalyzedAt, before, after);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsReport_WithNonNullCollections()
    {
        var report = await _sut.AnalyzeAsync(Builders.Tree(Builders.Dir()));

        Assert.NotNull(report.FileTypeDistribution);
        Assert.NotNull(report.FileTypeSizeMap);
        Assert.NotNull(report.IgnoredFiles);
        Assert.NotNull(report.SystemFiles);
        Assert.NotNull(report.RequiredLabels);
    }

    // ── 2. FileTypeDistribution mirrors ExtensionCounts ───────────────────────

    [Fact]
    public async Task AnalyzeAsync_FileTypeDistribution_MatchesTreeExtensionCounts()
    {
        var root = Builders.Dir("Root", children: new List<FileNode>
        {
            Builders.File("a.pdf"),
            Builders.File("b.pdf"),
            Builders.File("c.txt"),
        });
        var metrics = Builders.MetricsFromRoot(root);
        var tree    = Builders.Tree(root, metrics);

        var report = await _sut.AnalyzeAsync(tree);

        Assert.Equal(metrics.ExtensionCounts["pdf"], report.FileTypeDistribution["pdf"]);
        Assert.Equal(metrics.ExtensionCounts["txt"], report.FileTypeDistribution["txt"]);
    }

    [Fact]
    public async Task AnalyzeAsync_FileTypeSizeMap_MatchesTreeExtensionSizes()
    {
        var root = Builders.Dir("Root", children: new List<FileNode>
        {
            Builders.File("a.pdf", sizeBytes: 5000),
            Builders.File("b.pdf", sizeBytes: 3000),
        });
        var metrics = Builders.MetricsFromRoot(root);
        var tree    = Builders.Tree(root, metrics);

        var report = await _sut.AnalyzeAsync(tree);

        Assert.Equal(8000L, report.FileTypeSizeMap["pdf"]);
    }

    // ── 3. SystemFiles and IgnoredFiles ──────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_SystemFiles_ContainsSystemFlaggedNodes()
    {
        var systemFile = Builders.File("pagefile.sys", attrs: FileAttributes.System);
        var root       = Builders.Dir("Root", children: new List<FileNode> { systemFile });
        var tree       = Builders.Tree(root);

        var report = await _sut.AnalyzeAsync(tree);

        Assert.Contains(systemFile, report.SystemFiles);
    }

    [Fact]
    public async Task AnalyzeAsync_IgnoredFiles_ContainsHiddenFlaggedNodes()
    {
        var hiddenFile = Builders.File(".hidden", attrs: FileAttributes.Hidden);
        var root       = Builders.Dir("Root", children: new List<FileNode> { hiddenFile });
        var tree       = Builders.Tree(root);

        var report = await _sut.AnalyzeAsync(tree);

        Assert.Contains(hiddenFile, report.IgnoredFiles);
    }

    [Fact]
    public async Task AnalyzeAsync_NormalFiles_AreNotInSystemOrIgnoredLists()
    {
        var normalFile = Builders.File("readme.txt");
        var root       = Builders.Dir("Root", children: new List<FileNode> { normalFile });
        var tree       = Builders.Tree(root);

        var report = await _sut.AnalyzeAsync(tree);

        Assert.DoesNotContain(normalFile, report.SystemFiles);
        Assert.DoesNotContain(normalFile, report.IgnoredFiles);
    }

    // ── 4. RequiredLabels heuristic ───────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_RequiredLabels_NotGeneratedForExtensionWithCountLessThanOrEqualTo2()
    {
        // Only 2 .mp4 files — threshold is > 2 so no label should be generated
        var root = Builders.Dir("Root", children: new List<FileNode>
        {
            Builders.File("a.mp4"),
            Builders.File("b.mp4"),
        });
        var metrics = Builders.MetricsFromRoot(root);
        var tree    = Builders.Tree(root, metrics);

        var report = await _sut.AnalyzeAsync(tree);

        Assert.DoesNotContain("MP4s", report.RequiredLabels);
    }

    [Fact]
    public async Task AnalyzeAsync_RequiredLabels_GeneratedForExtensionWithCountAbove2()
    {
        var root = Builders.Dir("Root", children: new List<FileNode>
        {
            Builders.File("a.mp4"),
            Builders.File("b.mp4"),
            Builders.File("c.mp4"),
        });
        var metrics = Builders.MetricsFromRoot(root);
        var tree    = Builders.Tree(root, metrics);

        var report = await _sut.AnalyzeAsync(tree);

        Assert.Contains("MP4s", report.RequiredLabels);
    }

    [Fact]
    public async Task AnalyzeAsync_RequiredLabels_AreDistinct()
    {
        // Three identical extension entries in metrics — labels shouldn't duplicate
        var metrics = new FolderAnalysisMetrics();
        metrics.ExtensionCounts["pdf"] = 5;
        metrics.ExtensionSizes["pdf"]  = 50000;
        var tree = Builders.Tree(Builders.Dir(), metrics);

        var report = await _sut.AnalyzeAsync(tree);

        Assert.Equal(report.RequiredLabels.Count, report.RequiredLabels.Distinct().Count());
    }

    // ── 5. Empty tree ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_EmptyTree_ReturnsEmptyCollections()
    {
        var tree   = Builders.Tree(Builders.Dir("Empty"));
        var report = await _sut.AnalyzeAsync(tree);

        Assert.Empty(report.FileTypeDistribution);
        Assert.Empty(report.SystemFiles);
        Assert.Empty(report.IgnoredFiles);
        Assert.Empty(report.RequiredLabels);
    }

    // ── 6. Deeply nested files ────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DeepSystemFile_IsFoundByRecursiveTraversal()
    {
        // Three levels deep
        var deepSystemFile = Builders.File("deep.sys", attrs: FileAttributes.System);
        var level2 = Builders.Dir("Level2", children: new List<FileNode> { deepSystemFile });
        var level1 = Builders.Dir("Level1", children: new List<FileNode> { level2 });
        var root   = Builders.Dir("Root",   children: new List<FileNode> { level1 });
        var tree   = Builders.Tree(root);

        var report = await _sut.AnalyzeAsync(tree);

        Assert.Contains(deepSystemFile, report.SystemFiles);
    }

    // ── 7. Cancellation ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_WithCancelledToken_ThrowsOperationCancelledException()
    {
        // Build a large-ish tree to give cancellation a chance to fire
        var children = new List<FileNode>();
        for (int i = 0; i < 200; i++)
            children.Add(Builders.File($"file{i}.txt"));
        var root = Builders.Dir("Root", children: children);
        var tree = Builders.Tree(root);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.AnalyzeAsync(tree, cts.Token));
    }
}
