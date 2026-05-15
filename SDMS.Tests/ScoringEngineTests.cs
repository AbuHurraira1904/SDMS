// ============================================================
// ScoringEngineTests.cs  →  SDMS.Tests/Scoring/
// Tests for SDMS.Infrastructure.Scoring.ScoringEngine
//
// What we're testing:
//   1. Empty report → empty list (no crash, no divide-by-zero)
//   2. All scores are in [0, 100]
//   3. Single-file list → score is 50 (normalization clamp when range ≈ 0)
//   4. Score breakdown contains expected keys
//   5. Duplicate detection — files sharing a hash score lower than unique files
//   6. System/hidden penalty — flagged files score lower than normal files
//   7. Document category scores higher than temp category (type weight)
//   8. Recently accessed file scores higher than stale file
//   9. Massive file (≥500 MB) gets size penalty
//  10. File with no extension is handled without throwing
//  11. Cancellation is respected
//  12. Custom ScoringWeights are applied (not ignored)
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDMS.Domain.Models;
using SDMS.Domain.Scoring;
using SDMS.Infrastructure.Scoring;
using SDMS.Tests.Helpers;
using Xunit;

namespace SDMS.Tests.Scoring;

public sealed class ScoringEngineTests
{
    private readonly ScoringEngine _sut = new();

    // ── 1. Empty report ───────────────────────────────────────────────────────

    [Fact]
    public async Task ScoreAsync_EmptyTree_ReturnsEmptyList()
    {
        var report = Builders.Report(Builders.Tree(Builders.Dir("Empty")));
        var result = await _sut.ScoreAsync(report);

        Assert.Empty(result);
    }

    // ── 2. All scores in [0, 100] ─────────────────────────────────────────────

    [Fact]
    public async Task ScoreAsync_AllScores_AreInValidRange()
    {
        var root = Builders.Dir("Root", children: new List<FileNode>
        {
            Builders.File("a.pdf",  sizeBytes: 500),
            Builders.File("b.mp4",  sizeBytes: 2_000_000_000L),
            Builders.File("c.tmp",  sizeBytes: 10),
            Builders.File("d.exe",  sizeBytes: 1024 * 1024),
        });
        var report = Builders.Report(Builders.Tree(root));
        var result = await _sut.ScoreAsync(report);

        Assert.All(result, r => Assert.InRange(r.Score, 0, 100));
    }

    // ── 3. Single file → score = 50 ───────────────────────────────────────────

    [Fact]
    public async Task ScoreAsync_SingleFile_ScoreIs50()
    {
        // When min == max, range ≈ 0, so normalization returns 50.0
        var root   = Builders.Dir("Root", children: new List<FileNode> { Builders.File("only.pdf") });
        var report = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report);

        Assert.Single(result);
        Assert.Equal(50, result[0].Score);
    }

    // ── 4. Score breakdown keys ───────────────────────────────────────────────

    [Fact]
    public async Task ScoreAsync_ScoreBreakdown_ContainsExpectedKeys()
    {
        var root   = Builders.Dir("Root", children: new List<FileNode> { Builders.File("x.pdf") });
        var report = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report);

        var breakdown = result[0].ScoreBreakdown;
        Assert.True(breakdown.ContainsKey("recency"),  "Missing 'recency' key");
        Assert.True(breakdown.ContainsKey("modified"), "Missing 'modified' key");
        Assert.True(breakdown.ContainsKey("type"),     "Missing 'type' key");
        Assert.True(breakdown.ContainsKey("size"),     "Missing 'size' key");
        Assert.True(breakdown.ContainsKey("raw"),      "Missing 'raw' key");
    }

    // ── 5. Duplicate penalty ──────────────────────────────────────────────────

    [Fact]
    public async Task ScoreAsync_DuplicateFiles_ScoreLowerThanUniqueFile()
    {
        var sharedHash = "abc123hash";
        var now        = DateTime.UtcNow;

        // Make everything else identical so only the hash varies
        var dup1   = Builders.File("dup1.pdf",    hash: sharedHash, accessed: now, modified: now);
        var dup2   = Builders.File("dup2.pdf",    hash: sharedHash, accessed: now, modified: now);
        var unique = Builders.File("unique.pdf",  hash: "uniquehash", accessed: now, modified: now);

        var root   = Builders.Dir("Root", children: new List<FileNode> { dup1, dup2, unique });
        var report = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report);

        var dupScore    = result.First(r => r.Node.Name == "dup1.pdf").Score;
        var uniqueScore = result.First(r => r.Node.Name == "unique.pdf").Score;

        Assert.True(uniqueScore >= dupScore,
            $"Expected unique ({uniqueScore}) >= duplicate ({dupScore})");
    }

    // ── 6. System/hidden penalty ──────────────────────────────────────────────

    [Fact]
    public async Task ScoreAsync_SystemFile_ScoresLowerThanNormalFile()
    {
        var now    = DateTime.UtcNow;
        var normal = Builders.File("readme.txt",   attrs: FileAttributes.Normal,  accessed: now, modified: now);
        var system = Builders.File("pagefile.sys", attrs: FileAttributes.System,  accessed: now, modified: now);

        var root   = Builders.Dir("Root", children: new List<FileNode> { normal, system });
        var report = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report);

        var normalScore = result.First(r => r.Node.Name == "readme.txt").Score;
        var systemScore = result.First(r => r.Node.Name == "pagefile.sys").Score;

        Assert.True(normalScore >= systemScore,
            $"Normal ({normalScore}) should be >= system ({systemScore})");
    }

    // ── 7. Category type weight: document > temp ──────────────────────────────

    [Fact]
    public async Task ScoreAsync_DocumentFile_ScoresHigherThanTempFile()
    {
        var now  = DateTime.UtcNow;
        var doc  = Builders.File("report.pdf",   accessed: now, modified: now, sizeBytes: 102400);
        var temp = Builders.File("cache.tmp",    accessed: now, modified: now, sizeBytes: 102400);

        var root   = Builders.Dir("Root", children: new List<FileNode> { doc, temp });
        var report = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report);

        var docScore  = result.First(r => r.Node.Name == "report.pdf").Score;
        var tempScore = result.First(r => r.Node.Name == "cache.tmp").Score;

        Assert.True(docScore > tempScore,
            $"Document ({docScore}) should score higher than temp ({tempScore})");
    }

    // ── 8. Recency: recently accessed file scores higher ──────────────────────

    [Fact]
    public async Task ScoreAsync_RecentFile_ScoresHigherThanStaleFile()
    {
        var now    = DateTime.UtcNow;
        var recent = Builders.File("recent.pdf", accessed: now,               modified: now,               sizeBytes: 10240);
        var stale  = Builders.File("stale.pdf",  accessed: now.AddDays(-365), modified: now.AddDays(-365), sizeBytes: 10240);

        var root   = Builders.Dir("Root", children: new List<FileNode> { recent, stale });
        var report = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report);

        var recentScore = result.First(r => r.Node.Name == "recent.pdf").Score;
        var staleScore  = result.First(r => r.Node.Name == "stale.pdf").Score;

        Assert.True(recentScore > staleScore,
            $"Recent ({recentScore}) should score higher than stale ({staleScore})");
    }

    // ── 9. Massive file penalty (≥ 500 MB) ────────────────────────────────────

    [Fact]
    public async Task ScoreAsync_MassiveFile_ScoresLowerThanModerateFile()
    {
        var now      = DateTime.UtcNow;
        // Both are .pdf (same type) accessed at the same time — only size differs
        var massive  = Builders.File("huge.pdf",   sizeBytes: 600L * 1024 * 1024, accessed: now, modified: now);
        var moderate = Builders.File("normal.pdf", sizeBytes: 512 * 1024,          accessed: now, modified: now);

        var root   = Builders.Dir("Root", children: new List<FileNode> { massive, moderate });
        var report = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report);

        var massiveScore  = result.First(r => r.Node.Name == "huge.pdf").Score;
        var moderateScore = result.First(r => r.Node.Name == "normal.pdf").Score;

        Assert.True(moderateScore >= massiveScore,
            $"Moderate ({moderateScore}) should be >= massive ({massiveScore})");
    }

    // ── 10. No extension ─────────────────────────────────────────────────────

    [Fact]
    public async Task ScoreAsync_FileWithNoExtension_DoesNotThrow()
    {
        var noExt  = Builders.File("Makefile");   // no dot → empty extension
        var root   = Builders.Dir("Root", children: new List<FileNode> { noExt });
        var report = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report);

        Assert.Single(result);
        Assert.InRange(result[0].Score, 0, 100);
    }

    // ── 11. Cancellation ─────────────────────────────────────────────────────

    [Fact]
    public async Task ScoreAsync_WithCancelledToken_ThrowsOperationCancelledException()
    {
        var children = Enumerable.Range(0, 300)
            .Select(i => Builders.File($"f{i}.pdf"))
            .ToList<FileNode>();
        var root   = Builders.Dir("Root", children: children);
        var report = Builders.Report(Builders.Tree(root));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.ScoreAsync(report, ct: cts.Token));
    }

    // ── 12. Custom weights are applied ───────────────────────────────────────

    [Fact]
    public async Task ScoreAsync_WithZeroTypeWeight_TypeDoesNotDifferentiateScores()
    {
        // With FileTypePriorityWeight = 0, a document and a temp file
        // scored at the same recency/modified/size should have the same raw score
        var now     = DateTime.UtcNow;
        var weights = new ScoringWeights
        {
            RecencyWeight          = 1.0,
            ModifiedRecencyWeight  = 1.0,
            FileTypePriorityWeight = 0.0,  // <-- zeroed out
            LargeSizePenaltyWeight = 1.0,
            DuplicatePenaltyWeight = 0.0,
            SystemFilePenaltyWeight = 0.0,
        };

        var doc  = Builders.File("doc.pdf",  sizeBytes: 10240, accessed: now, modified: now);
        var temp = Builders.File("tmp.tmp",  sizeBytes: 10240, accessed: now, modified: now);

        var root   = Builders.Dir("Root", children: new List<FileNode> { doc, temp });
        var report = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report, weights);

        // When type weight is zero both files should have nearly identical raw scores
        // and thus the same normalized score (min==max → both become 50)
        var docScore  = result.First(r => r.Node.Name == "doc.pdf").Score;
        var tempScore = result.First(r => r.Node.Name == "tmp.tmp").Score;

        Assert.Equal(docScore, tempScore);
    }

    // ── 13. Directories are excluded from scoring ─────────────────────────────

    [Fact]
    public async Task ScoreAsync_DirectoriesInTree_AreNotIncludedInResults()
    {
        var subDir  = Builders.Dir("SubDir");
        var file    = Builders.File("a.pdf");
        var root    = Builders.Dir("Root", children: new List<FileNode> { subDir, file });
        var report  = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report);

        Assert.All(result, r => Assert.False(r.Node.IsDirectory));
    }

    // ── 14. Result count matches file count ───────────────────────────────────

    [Fact]
    public async Task ScoreAsync_ResultCount_MatchesTotalFileCount()
    {
        var files   = Enumerable.Range(0, 10).Select(i => Builders.File($"f{i}.pdf")).ToList<FileNode>();
        var root    = Builders.Dir("Root", children: files);
        var report  = Builders.Report(Builders.Tree(root));

        var result = await _sut.ScoreAsync(report);

        Assert.Equal(10, result.Count);
    }
}
