// ============================================================
// ExecutionEngineTests.cs  →  SDMS.Tests/Execution/
// Tests for SDMS.Infrastructure.Execution.ExecutionEngine
//
// NOTE: ExecutionEngine performs real filesystem operations.
// Each test creates a fresh temp directory via TempDir and
// cleans it up in Dispose(). No mocking — these are integration-
// style unit tests against the real OS layer (same approach
// the engine will use in production).
//
// What we're testing:
//   1.  State is Idle on construction
//   2.  DryRun mode — no filesystem changes, all results succeed
//   3.  Move operation — file is relocated
//   4.  Delete with staging — file moves to .sdms_staging, not deleted
//   5.  Delete without staging — file is permanently removed
//   6.  NewFolder operation — directory is created
//   7.  ExecutionLog.SuccessCount matches number of ops on success
//   8.  State transitions to Completed on full success
//   9.  State transitions to PartialFailure when an op fails
//  10.  Preflight — valid plan returns IsValid = true
//  11.  Preflight — missing source path returns error
//  12.  Preflight — cross-volume move returns error
//  13.  GetUniquePath — collision produces "(1)" suffix
//  14.  Cancellation — stops execution mid-run
//  15.  RollbackAsync — state transitions to RolledBack
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDMS.Domain.Execution;
using SDMS.Domain.Models;
using SDMS.Infrastructure.Execution;
using SDMS.Tests.Helpers;
using Xunit;

namespace SDMS.Tests.Execution;

/// <summary>
/// Each test gets a fresh isolated temp directory.
/// </summary>
public sealed class ExecutionEngineTests : IDisposable
{
    private readonly string _tmp;
    private readonly ExecutionEngine _sut = new();

    public ExecutionEngineTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), $"SDMS_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmp))
            Directory.Delete(_tmp, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string TempFile(string name, string content = "test")
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string TempPath(string name) => Path.Combine(_tmp, name);

    private FinalizedPlan PlanWith(params PlannedOperation[] ops) =>
        new FinalizedPlan
        {
            Id           = Guid.NewGuid(),
            SourcePlanId = Guid.NewGuid(),
            Operations   = ops.ToList(),
            FinalizedAt  = DateTime.UtcNow,
            OriginalPlan = Builders.ProposedPlan(),
        };

    // ── 1. Initial state ──────────────────────────────────────────────────────

    [Fact]
    public void State_IsIdle_OnConstruction()
    {
        Assert.Equal(ExecutionState.Idle, _sut.State);
    }

    // ── 2. DryRun — no filesystem changes ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DryRun_DoesNotMoveFile()
    {
        var src  = TempFile("dryrun.txt");
        var dest = TempPath("moved\\dryrun.txt");

        var op = new PlannedOperation
        {
            Id              = Guid.NewGuid(),
            Type            = OpType.Move,
            SourcePath      = src,
            DestinationPath = dest,
            Reason          = "dry run test",
            Status          = OpStatus.Confirmed,
        };
        var plan    = PlanWith(op);
        var options = new ExecutionOptions { DryRun = true };

        var log = await _sut.ExecuteAsync(plan, options);

        Assert.True(File.Exists(src),   "Source should still exist in DryRun");
        Assert.False(File.Exists(dest), "Dest should not exist in DryRun");
        Assert.Equal(1, log.SuccessCount);
    }

    // ── 3. Move operation ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MoveOp_RelocatesFile()
    {
        var src  = TempFile("move_me.txt");
        var dest = TempPath(Path.Combine("SubDir", "move_me.txt"));

        var op = new PlannedOperation
        {
            Id              = Guid.NewGuid(),
            Type            = OpType.Move,
            SourcePath      = src,
            DestinationPath = dest,
            Reason          = "organize",
            Status          = OpStatus.Confirmed,
        };
        var log = await _sut.ExecuteAsync(PlanWith(op));

        Assert.False(File.Exists(src),  "Source should be gone after move");
        Assert.True(File.Exists(dest),  "Dest should exist after move");
        Assert.Equal(1, log.SuccessCount);
    }

    // ── 4. Delete with staging ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DeleteWithStaging_MovesToStagingNotDeleted()
    {
        var src = TempFile("to_stage.txt");

        var op = new PlannedOperation
        {
            Id         = Guid.NewGuid(),
            Type       = OpType.Delete,
            SourcePath = src,
            Reason     = "cleanup",
            Status     = OpStatus.Confirmed,
        };
        var options = new ExecutionOptions
        {
            UseStagingForDeletes = true,
            StagingAreaPath      = Path.Combine(_tmp, ".sdms_staging"),
        };
        var log = await _sut.ExecuteAsync(PlanWith(op), options);

        Assert.False(File.Exists(src), "Original should be gone from source location");
        Assert.Equal(1, log.SuccessCount);

        // The staging path from the result should exist
        var result = log.Results[0];
        Assert.NotNull(result.StagingPath);
        Assert.True(File.Exists(result.StagingPath), "File should be in staging area");
    }

    // ── 5. Delete without staging ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DeleteWithoutStaging_PermanentlyRemovesFile()
    {
        var src = TempFile("permanent_delete.txt");

        var op = new PlannedOperation
        {
            Id         = Guid.NewGuid(),
            Type       = OpType.Delete,
            SourcePath = src,
            Reason     = "hard delete",
            Status     = OpStatus.Confirmed,
        };
        var options = new ExecutionOptions { UseStagingForDeletes = false };
        var log     = await _sut.ExecuteAsync(PlanWith(op), options);

        Assert.False(File.Exists(src));
        Assert.Equal(1, log.SuccessCount);
    }

    // ── 6. NewFolder operation ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NewFolderOp_CreatesDirectory()
    {
        var dest = TempPath("NewCreatedFolder");

        var op = new PlannedOperation
        {
            Id              = Guid.NewGuid(),
            Type            = OpType.NewFolder,
            DestinationPath = dest,
            Reason          = "create structure",
            Status          = OpStatus.Confirmed,
        };
        var log = await _sut.ExecuteAsync(PlanWith(op));

        Assert.True(Directory.Exists(dest));
        Assert.Equal(1, log.SuccessCount);
    }

    // ── 7. SuccessCount matches operations ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MultipleOps_SuccessCountMatchesTotal()
    {
        var src1  = TempFile("f1.txt");
        var src2  = TempFile("f2.txt");
        var dest1 = TempPath(Path.Combine("Out", "f1.txt"));
        var dest2 = TempPath(Path.Combine("Out", "f2.txt"));

        var ops = new[]
        {
            new PlannedOperation { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = src1, DestinationPath = dest1, Reason = "r", Status = OpStatus.Confirmed },
            new PlannedOperation { Id = Guid.NewGuid(), Type = OpType.Move, SourcePath = src2, DestinationPath = dest2, Reason = "r", Status = OpStatus.Confirmed },
        };
        var log = await _sut.ExecuteAsync(PlanWith(ops));

        Assert.Equal(2, log.SuccessCount);
        Assert.Equal(0, log.FailureCount);
    }

    // ── 8. State → Completed ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OnFullSuccess_StateIsCompleted()
    {
        var src  = TempFile("state_test.txt");
        var dest = TempPath(Path.Combine("Out", "state_test.txt"));

        var op = new PlannedOperation
        {
            Id = Guid.NewGuid(), Type = OpType.Move,
            SourcePath = src, DestinationPath = dest,
            Reason = "r", Status = OpStatus.Confirmed,
        };
        await _sut.ExecuteAsync(PlanWith(op));

        Assert.Equal(ExecutionState.Completed, _sut.State);
    }

    // ── 9. State → PartialFailure ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenOpFails_StateIsPartialFailure()
    {
        var goodSrc  = TempFile("good.txt");
        var goodDest = TempPath(Path.Combine("Out", "good.txt"));

        var ops = new[]
        {
            new PlannedOperation
            {
                Id = Guid.NewGuid(), Type = OpType.Move,
                SourcePath = goodSrc, DestinationPath = goodDest,
                Reason = "r", Status = OpStatus.Confirmed,
            },
            new PlannedOperation
            {
                Id = Guid.NewGuid(), Type = OpType.Move,
                SourcePath = "C:\\does\\not\\exist.txt",   // will throw
                DestinationPath = TempPath("out.txt"),
                Reason = "r", Status = OpStatus.Confirmed,
            },
        };
        var log = await _sut.ExecuteAsync(PlanWith(ops));

        Assert.Equal(ExecutionState.PartialFailure, _sut.State);
        Assert.True(log.FailureCount > 0);
    }

    // ── 10. Preflight — valid plan ────────────────────────────────────────────

    [Fact]
    public async Task PreflightAsync_ValidPlan_ReturnsIsValidTrue()
    {
        var src  = TempFile("preflight.txt");
        var dest = TempPath(Path.Combine("Out", "preflight.txt"));

        var op = new PlannedOperation
        {
            Id = Guid.NewGuid(), Type = OpType.Move,
            SourcePath = src, DestinationPath = dest,
            Reason = "r", Status = OpStatus.Confirmed,
        };
        var result = await _sut.PreflightAsync(PlanWith(op));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // ── 11. Preflight — missing source ────────────────────────────────────────

    [Fact]
    public async Task PreflightAsync_MissingSource_ReturnsError()
    {
        var op = new PlannedOperation
        {
            Id = Guid.NewGuid(), Type = OpType.Move,
            SourcePath      = "C:\\DoesNotExist\\ghost.txt",
            DestinationPath = TempPath("ghost.txt"),
            Reason = "r", Status = OpStatus.Confirmed,
        };
        var result = await _sut.PreflightAsync(PlanWith(op));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Source not found"));
    }

    // ── 12. Preflight — cross-volume ──────────────────────────────────────────

    [Fact]
    public async Task PreflightAsync_CrossVolumeMove_ReturnsError()
    {
        // This test only makes sense on Windows (C:\ vs D:\).
        // On Linux all paths share the same root, so we skip.
        if (!OperatingSystem.IsWindows()) return;

        var src  = TempFile("crossvol.txt");                // C:\... or wherever temp is
        var dest = "D:\\Somewhere\\crossvol.txt";           // deliberately different drive

        var op = new PlannedOperation
        {
            Id = Guid.NewGuid(), Type = OpType.Move,
            SourcePath = src, DestinationPath = dest,
            Reason = "r", Status = OpStatus.Confirmed,
        };
        var result = await _sut.PreflightAsync(PlanWith(op));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Cross-volume"));
    }

    // ── 13. Collision suffix ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CollisionAtDest_AddsNumericSuffix()
    {
        // Create dest file first to force collision
        var src      = TempFile("collide.txt", "source content");
        var destDir  = Path.Combine(_tmp, "DestDir");
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, "collide.txt");
        File.WriteAllText(destPath, "existing content");  // collision

        var op = new PlannedOperation
        {
            Id = Guid.NewGuid(), Type = OpType.Move,
            SourcePath = src, DestinationPath = destPath,
            Reason = "r", Status = OpStatus.Confirmed,
        };
        var log = await _sut.ExecuteAsync(PlanWith(op));

        // The original dest still has its original content
        Assert.Equal("existing content", File.ReadAllText(destPath));
        // A "(1)" suffixed file should now exist
        var suffixed = Path.Combine(destDir, "collide (1).txt");
        Assert.True(File.Exists(suffixed), "Expected collision suffix file to exist");
        Assert.Equal(1, log.SuccessCount);
    }

    // ── 14. Cancellation stops execution ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithCancelledToken_StopsProcessing()
    {
        // Build many operations; cancellation will prevent all of them running
        var ops = Enumerable.Range(0, 50).Select(i =>
        {
            var src  = TempFile($"cancel_{i}.txt");
            var dest = TempPath(Path.Combine("CancelOut", $"cancel_{i}.txt"));
            return new PlannedOperation
            {
                Id = Guid.NewGuid(), Type = OpType.Move,
                SourcePath = src, DestinationPath = dest,
                Reason = "r", Status = OpStatus.Confirmed,
            };
        }).ToArray();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel before start

        var log = await _sut.ExecuteAsync(PlanWith(ops), ct: cts.Token);

        // With an already-cancelled token the loop exits immediately
        Assert.True(log.Results.Count < ops.Length,
            "Expected fewer results than total ops due to cancellation");
    }

    // ── 15. Rollback state ────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_TransitionsStateToRolledBack()
    {
        var dummyLog = new ExecutionLog
        {
            PlanId     = Guid.NewGuid(),
            StartedAt  = DateTime.UtcNow,
            FinalState = ExecutionState.PartialFailure,
        };

        await _sut.RollbackAsync(dummyLog);

        Assert.Equal(ExecutionState.RolledBack, _sut.State);
    }
}
