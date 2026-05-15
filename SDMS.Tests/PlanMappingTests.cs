// ============================================================
// PlanMappingTests.cs  →  SDMS.Tests/Brain/
// Tests for SDMS.Infrastructure.Brain.PlanMappingExtensions
//
// What we're testing:
//   1. ToPlannedOperation — all fields mapped correctly
//   2. ToPlannedOperation — OpType parsing is case-insensitive
//   3. ToPlannedOperation — unknown OpType throws InvalidOperationException
//   4. ToPlannedOperation — missing/invalid Status defaults to Pending
//   5. ToPlannedOperation — IsUserAdded is always false
//   6. ToFinalizedPlan — operations count matches PlanOutput.Operations count
//   7. ToFinalizedPlan — FinalizedAt is recent (not default DateTime)
//   8. ToFinalizedPlan — each operation gets a unique Guid
//   9. ToFinalizedPlan — null Destination is preserved as null
// ============================================================

using System;
using System.Linq;
using SDMS.Domain.Brain;
using SDMS.Domain.Models;
using SDMS.Infrastructure.Brain;
using Xunit;

namespace SDMS.Tests.Brain;

public sealed class PlanMappingTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PlanOp MakePlanOp(
        string opType      = "Move",
        string source      = "C:\\Root\\file.txt",
        string? dest       = "C:\\Root\\Docs\\file.txt",
        string reason      = "Organizing",
        double confidence  = 0.9,
        float  importance  = 7.0f,
        string status      = "Pending") =>
        new PlanOp
        {
            OpType      = opType,
            Source      = source,
            Destination = dest,
            Reason      = reason,
            Confidence  = confidence,
            Importance  = importance,
            Status      = status,
        };

    private static PlanOutput MakePlanOutput(params PlanOp[] ops) =>
        new PlanOutput
        {
            ScanRoot          = "C:\\Root",
            TotalFilesScanned = ops.Length,
            Safety            = new SafetySummary
            {
                TotalOperations    = ops.Length,
                SafeOperations     = ops.Length,
                BlockedOperations  = 0,
                AffectedFiles      = ops.Length,
            },
            Operations = ops.ToList(),
        };

    // ── 1. Field mapping ──────────────────────────────────────────────────────

    [Fact]
    public void ToPlannedOperation_MapsAllFields_Correctly()
    {
        var raw = MakePlanOp(
            opType:     "Move",
            source:     "C:\\src\\file.pdf",
            dest:       "C:\\dst\\file.pdf",
            reason:     "Organize PDFs",
            confidence: 0.85,
            importance: 6.5f,
            status:     "Pending");

        var op = raw.ToPlannedOperation();

        Assert.Equal(OpType.Move,        op.Type);
        Assert.Equal("C:\\src\\file.pdf", op.SourcePath);
        Assert.Equal("C:\\dst\\file.pdf", op.DestinationPath);
        Assert.Equal("Organize PDFs",    op.Reason);
        Assert.Equal(0.85,               op.Confidence, precision: 5);
        Assert.Equal(6.5f,               op.Importance);
        Assert.Equal(OpStatus.Pending,   op.Status);
    }

    // ── 2. OpType parsing is case-insensitive ─────────────────────────────────

    [Theory]
    [InlineData("move",        OpType.Move)]
    [InlineData("MOVE",        OpType.Move)]
    [InlineData("Delete",      OpType.Delete)]
    [InlineData("newFolder",   OpType.NewFolder)]
    [InlineData("DeleteFolder",OpType.DeleteFolder)]
    [InlineData("MoveFolder",  OpType.MoveFolder)]
    [InlineData("MergeFolder", OpType.MergeFolder)]
    public void ToPlannedOperation_OpTypeParsing_IsCaseInsensitive(string raw, OpType expected)
    {
        var op = MakePlanOp(opType: raw).ToPlannedOperation();
        Assert.Equal(expected, op.Type);
    }

    // ── 3. Unknown OpType throws ─────────────────────────────────────────────

    [Fact]
    public void ToPlannedOperation_UnknownOpType_ThrowsInvalidOperationException()
    {
        var raw = MakePlanOp(opType: "Teleport");
        Assert.Throws<InvalidOperationException>(() => raw.ToPlannedOperation());
    }

    // ── 4. Invalid Status defaults to Pending ─────────────────────────────────

    [Theory]
    [InlineData("UnknownStatus")]
    [InlineData("")]
    [InlineData("DONE")]
    public void ToPlannedOperation_InvalidStatus_DefaultsToPending(string status)
    {
        var raw = MakePlanOp(status: status);
        var op  = raw.ToPlannedOperation();
        Assert.Equal(OpStatus.Pending, op.Status);
    }

    // ── 5. IsUserAdded is always false ────────────────────────────────────────

    [Fact]
    public void ToPlannedOperation_IsUserAdded_IsAlwaysFalse()
    {
        var op = MakePlanOp().ToPlannedOperation();
        Assert.False(op.IsUserAdded);
    }

    // ── 6. ToFinalizedPlan — operation count ──────────────────────────────────

    [Fact]
    public void ToFinalizedPlan_OperationCount_MatchesPlanOutputOperations()
    {
        var planOutput = MakePlanOutput(
            MakePlanOp(source: "C:\\a.txt", dest: "C:\\Docs\\a.txt"),
            MakePlanOp(source: "C:\\b.txt", dest: "C:\\Docs\\b.txt"),
            MakePlanOp(opType: "Delete", source: "C:\\junk.tmp", dest: null));

        var finalized = planOutput.ToFinalizedPlan();

        Assert.Equal(3, finalized.Operations.Count);
    }

    // ── 7. FinalizedAt is recent ──────────────────────────────────────────────

    [Fact]
    public void ToFinalizedPlan_FinalizedAt_IsRecent()
    {
        var before    = DateTime.UtcNow.AddSeconds(-1);
        var finalized = MakePlanOutput(MakePlanOp()).ToFinalizedPlan();
        var after     = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(finalized.FinalizedAt, before, after);
    }

    // ── 8. Each operation gets a unique Guid ──────────────────────────────────

    [Fact]
    public void ToFinalizedPlan_EachOperation_HasUniqueId()
    {
        var planOutput = MakePlanOutput(
            MakePlanOp(source: "C:\\a.txt", dest: "C:\\Docs\\a.txt"),
            MakePlanOp(source: "C:\\b.txt", dest: "C:\\Docs\\b.txt"),
            MakePlanOp(source: "C:\\c.txt", dest: "C:\\Docs\\c.txt"));

        var finalized = planOutput.ToFinalizedPlan();
        var ids       = finalized.Operations.Select(o => o.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // ── 9. Null destination preserved ─────────────────────────────────────────

    [Fact]
    public void ToPlannedOperation_NullDestination_PreservedAsNull()
    {
        var raw = MakePlanOp(opType: "Delete", dest: null);
        var op  = raw.ToPlannedOperation();

        Assert.Null(op.DestinationPath);
    }
}
