// ============================================================
// PlanEditorTests.cs  →  SDMS.Tests/PlanEditor/
// Tests for SDMS.Infrastructure.PlanEditor.PlanEditor
//
// What we're testing:
//   1.  Constructor loads operations from ProposedPlan (deep copy, not reference)
//   2.  UpdateOperation — happy path
//   3.  UpdateOperation — unknown ID returns Fail result
//   4.  AddOperation — appended to list, snapshot created
//   5.  RemoveOperation — removes by ID
//   6.  RemoveOperation — unknown ID returns Fail result
//   7.  SetStatus — changes operation status
//   8.  SetStatus — unknown ID returns Fail result
//   9.  Undo — reverts the last edit
//   10. Undo — when nothing to undo, is a no-op (no crash)
//   11. Redo — re-applies reverted edit
//   12. Redo — new edit clears redo stack
//   13. CanUndo / CanRedo state machine
//   14. ValidatePlan — collision detection (two ops to same dest)
//   15. ValidatePlan — Move with null source returns error
//   16. ValidatePlan — Skipped ops excluded from collision check
//   17. Finalize — only Confirmed and Pending ops included
//   18. Finalize — ordering: NewFolder before Move before Delete
//   19. Finalize — throws when plan has validation errors
//   20. Finalize — returned FinalizedPlan references the original ProposedPlan
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using SDMS.Domain.Models;
using SDMS.Domain.PlanEditor;
using SDMS.Infrastructure.PlanEditor;
using SDMS.Tests.Helpers;
using Xunit;

namespace SDMS.Tests.PlanEditor;

public sealed class PlanEditorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static global::SDMS.Infrastructure.PlanEditor.PlanEditor MakeEditor(
        List<PlannedOperation>? ops = null)
    {
        var plan = Builders.ProposedPlan(ops ?? new List<PlannedOperation>());
        return new global::SDMS.Infrastructure.PlanEditor.PlanEditor(plan);
    }

    // ── 1. Constructor — deep copy ────────────────────────────────────────────

    [Fact]
    public void Constructor_LoadsOperations_AsDeepCopy()
    {
        var op   = Builders.MoveOp();
        var plan = Builders.ProposedPlan(new List<PlannedOperation> { op });
        var editor = new global::SDMS.Infrastructure.PlanEditor.PlanEditor(plan);

        // Mutating the original plan's list should not affect the editor's copy
        plan.Operations.Clear();

        Assert.Single(editor.CurrentOperations);
    }

    // ── 2. UpdateOperation — happy path ───────────────────────────────────────

    [Fact]
    public void UpdateOperation_ValidId_ReplacesOperation()
    {
        var original = Builders.MoveOp(src: "C:\\src\\a.txt", dest: "C:\\dst\\a.txt");
        var editor   = MakeEditor(new List<PlannedOperation> { original });

        var updated = original.Clone();
        updated.DestinationPath = "C:\\new\\a.txt";

        var result = editor.UpdateOperation(original.Id, updated);

        Assert.True(result.IsValid);
        Assert.Equal("C:\\new\\a.txt", editor.CurrentOperations[0].DestinationPath);
    }

    [Fact]
    public void UpdateOperation_ValidId_CreatesUndoSnapshot()
    {
        var op     = Builders.MoveOp();
        var editor = MakeEditor(new List<PlannedOperation> { op });

        Assert.False(editor.CanUndo);
        editor.UpdateOperation(op.Id, op.Clone());
        Assert.True(editor.CanUndo);
    }

    // ── 3. UpdateOperation — unknown ID ──────────────────────────────────────

    [Fact]
    public void UpdateOperation_UnknownId_ReturnsFail()
    {
        var editor = MakeEditor();
        var result = editor.UpdateOperation(Guid.NewGuid(), Builders.MoveOp());

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    // ── 4. AddOperation ───────────────────────────────────────────────────────

    [Fact]
    public void AddOperation_AppendsToCurrentOperations()
    {
        var editor = MakeEditor();
        var op     = Builders.NewFolderOp();

        editor.AddOperation(op);

        Assert.Single(editor.CurrentOperations);
        Assert.Equal(op.Id, editor.CurrentOperations[0].Id);
    }

    [Fact]
    public void AddOperation_CreatesUndoSnapshot()
    {
        var editor = MakeEditor();
        editor.AddOperation(Builders.NewFolderOp());
        Assert.True(editor.CanUndo);
    }

    // ── 5. RemoveOperation — happy path ───────────────────────────────────────

    [Fact]
    public void RemoveOperation_ValidId_RemovesFromList()
    {
        var op     = Builders.MoveOp();
        var editor = MakeEditor(new List<PlannedOperation> { op });

        var result = editor.RemoveOperation(op.Id);

        Assert.True(result.IsValid);
        Assert.Empty(editor.CurrentOperations);
    }

    // ── 6. RemoveOperation — unknown ID ──────────────────────────────────────

    [Fact]
    public void RemoveOperation_UnknownId_ReturnsFail()
    {
        var editor = MakeEditor();
        var result = editor.RemoveOperation(Guid.NewGuid());

        Assert.False(result.IsValid);
    }

    // ── 7. SetStatus — changes status ─────────────────────────────────────────

    [Fact]
    public void SetStatus_ValidId_ChangesOperationStatus()
    {
        var op     = Builders.MoveOp(status: OpStatus.Pending);
        var editor = MakeEditor(new List<PlannedOperation> { op });

        editor.SetStatus(op.Id, OpStatus.Skipped);

        Assert.Equal(OpStatus.Skipped, editor.CurrentOperations[0].Status);
    }

    // ── 8. SetStatus — unknown ID ─────────────────────────────────────────────

    [Fact]
    public void SetStatus_UnknownId_ReturnsFail()
    {
        var editor = MakeEditor();
        var result = editor.SetStatus(Guid.NewGuid(), OpStatus.Skipped);

        Assert.False(result.IsValid);
    }

    // ── 9. Undo — reverts last edit ───────────────────────────────────────────

    [Fact]
    public void Undo_AfterUpdate_RevertsToOriginalDestination()
    {
        var op     = Builders.MoveOp(dest: "C:\\original\\a.txt");
        var editor = MakeEditor(new List<PlannedOperation> { op });

        var updated = op.Clone();
        updated.DestinationPath = "C:\\changed\\a.txt";
        editor.UpdateOperation(op.Id, updated);

        editor.Undo();

        Assert.Equal("C:\\original\\a.txt", editor.CurrentOperations[0].DestinationPath);
    }

    // ── 10. Undo — no-op when nothing to undo ────────────────────────────────

    [Fact]
    public void Undo_WhenNothingToUndo_DoesNotThrow()
    {
        var editor = MakeEditor();
        var ex     = Record.Exception(() => editor.Undo());
        Assert.Null(ex);
    }

    // ── 11. Redo — re-applies reverted edit ──────────────────────────────────

    [Fact]
    public void Redo_AfterUndo_ReappliesEdit()
    {
        var op     = Builders.MoveOp(dest: "C:\\original\\a.txt");
        var editor = MakeEditor(new List<PlannedOperation> { op });

        var updated = op.Clone();
        updated.DestinationPath = "C:\\changed\\a.txt";
        editor.UpdateOperation(op.Id, updated);

        editor.Undo();
        editor.Redo();

        Assert.Equal("C:\\changed\\a.txt", editor.CurrentOperations[0].DestinationPath);
    }

    // ── 12. New edit clears redo stack ────────────────────────────────────────

    [Fact]
    public void NewEdit_AfterUndo_ClearsRedoStack()
    {
        var op     = Builders.MoveOp();
        var editor = MakeEditor(new List<PlannedOperation> { op });

        editor.UpdateOperation(op.Id, op.Clone());
        editor.Undo();

        Assert.True(editor.CanRedo);

        // New edit — redo should now be cleared
        editor.AddOperation(Builders.NewFolderOp());

        Assert.False(editor.CanRedo);
    }

    // ── 13. CanUndo / CanRedo state machine ──────────────────────────────────

    [Fact]
    public void CanUndo_IsFalseInitially_TrueAfterEdit_FalseAfterFullUndo()
    {
        var op     = Builders.MoveOp();
        var editor = MakeEditor(new List<PlannedOperation> { op });

        Assert.False(editor.CanUndo);

        editor.UpdateOperation(op.Id, op.Clone());
        Assert.True(editor.CanUndo);

        editor.Undo();
        Assert.False(editor.CanUndo);
    }

    // ── 14. ValidatePlan — collision detection ────────────────────────────────

    [Fact]
    public void ValidatePlan_DuplicateDestination_ReturnsFail()
    {
        var dest = "C:\\Root\\Docs\\file.txt";
        var op1  = Builders.MoveOp(src: "C:\\a.txt", dest: dest, status: OpStatus.Confirmed);
        var op2  = Builders.MoveOp(src: "C:\\b.txt", dest: dest, status: OpStatus.Confirmed);
        var editor = MakeEditor(new List<PlannedOperation> { op1, op2 });

        var result = editor.ValidatePlan();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Collision"));
    }

    // ── 15. ValidatePlan — Move with null source ──────────────────────────────

    [Fact]
    public void ValidatePlan_MoveWithNullSource_ReturnsFail()
    {
        var badOp = new PlannedOperation
        {
            Id              = Guid.NewGuid(),
            Type            = OpType.Move,
            SourcePath      = null,   // missing
            DestinationPath = "C:\\Root\\dest.txt",
            Reason          = "bad",
            Status          = OpStatus.Confirmed,
        };
        var editor = MakeEditor(new List<PlannedOperation> { badOp });

        var result = editor.ValidatePlan();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Source path missing"));
    }

    // ── 16. Skipped ops excluded from collision check ─────────────────────────

    [Fact]
    public void ValidatePlan_SkippedOps_NotIncludedInCollisionCheck()
    {
        var dest  = "C:\\Root\\file.txt";
        var op1   = Builders.MoveOp(src: "C:\\a.txt", dest: dest, status: OpStatus.Skipped);
        var op2   = Builders.MoveOp(src: "C:\\b.txt", dest: dest, status: OpStatus.Confirmed);
        var editor = MakeEditor(new List<PlannedOperation> { op1, op2 });

        var result = editor.ValidatePlan();

        // op1 is skipped so only op2's destination is registered — no collision
        Assert.True(result.IsValid);
    }

    // ── 17. Finalize — only Confirmed and Pending ─────────────────────────────

    [Fact]
    public void Finalize_ExcludesSkippedOperations()
    {
        var confirmed = Builders.MoveOp(src: "C:\\a.txt", dest: "C:\\Docs\\a.txt", status: OpStatus.Confirmed);
        var skipped   = Builders.MoveOp(src: "C:\\b.txt", dest: "C:\\Junk\\b.txt", status: OpStatus.Skipped);
        var pending   = Builders.MoveOp(src: "C:\\c.txt", dest: "C:\\Docs\\c.txt", status: OpStatus.Pending);

        var plan   = Builders.ProposedPlan(new List<PlannedOperation> { confirmed, skipped, pending });
        var editor = new global::SDMS.Infrastructure.PlanEditor.PlanEditor(plan);

        var finalized = editor.Finalize(plan);

        Assert.Equal(2, finalized.Operations.Count);
        Assert.DoesNotContain(finalized.Operations, o => o.Id == skipped.Id);
    }

    // ── 18. Finalize — operation ordering ────────────────────────────────────

    [Fact]
    public void Finalize_OrdersOperations_NewFolderBeforeMoveBeforeDelete()
    {
        var del    = Builders.DeleteOp(status: OpStatus.Confirmed);
        var move   = Builders.MoveOp(src: "C:\\a.txt", dest: "C:\\Docs\\a.txt", status: OpStatus.Confirmed);
        var folder = Builders.NewFolderOp();  // already Confirmed

        var plan   = Builders.ProposedPlan(new List<PlannedOperation> { del, move, folder });
        var editor = new global::SDMS.Infrastructure.PlanEditor.PlanEditor(plan);

        var finalized = editor.Finalize(plan);
        var types     = finalized.Operations.Select(o => o.Type).ToList();

        Assert.Equal(OpType.NewFolder, types[0]);
        Assert.Equal(OpType.Move,      types[1]);
        Assert.Equal(OpType.Delete,    types[2]);
    }

    // ── 19. Finalize — throws on invalid plan ─────────────────────────────────

    [Fact]
    public void Finalize_WithValidationErrors_ThrowsInvalidOperationException()
    {
        // Two ops to the same destination — will fail ValidatePlan
        var dest = "C:\\Root\\file.txt";
        var op1  = Builders.MoveOp(src: "C:\\a.txt", dest: dest, status: OpStatus.Confirmed);
        var op2  = Builders.MoveOp(src: "C:\\b.txt", dest: dest, status: OpStatus.Confirmed);
        var plan = Builders.ProposedPlan(new List<PlannedOperation> { op1, op2 });
        var editor = new global::SDMS.Infrastructure.PlanEditor.PlanEditor(plan);

        Assert.Throws<InvalidOperationException>(() => editor.Finalize(plan));
    }

    // ── 20. Finalize — references original plan ───────────────────────────────

    [Fact]
    public void Finalize_FinalizedPlan_ReferencesOriginalProposedPlan()
    {
        var op     = Builders.MoveOp(src: "C:\\a.txt", dest: "C:\\Docs\\a.txt", status: OpStatus.Confirmed);
        var plan   = Builders.ProposedPlan(new List<PlannedOperation> { op });
        var editor = new global::SDMS.Infrastructure.PlanEditor.PlanEditor(plan);

        var finalized = editor.Finalize(plan);

        Assert.Equal(plan.Id, finalized.SourcePlanId);
        Assert.Same(plan, finalized.OriginalPlan);
    }
}
