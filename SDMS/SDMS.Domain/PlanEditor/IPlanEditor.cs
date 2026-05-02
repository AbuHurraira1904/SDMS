using SDMS.Domain.Models;

namespace SDMS.Domain.PlanEditor
{
    public interface IPlanEditor
    {
        // ── Session ──────────────────────────────────────────────────────────

        /// <summary>
        /// Load a ProposedPlan and create the initial snapshot (index 0).
        /// Resets any previous edit session.
        /// </summary>
        void LoadPlan(ProposedPlan plan);

        /// <summary>The current (latest) snapshot in the edit history.</summary>
        PlanSnapshot CurrentSnapshot { get; }

        /// <summary>All snapshots in order (index 0 = original plan).</summary>
        IReadOnlyList<PlanSnapshot> History { get; }

        // ── Editing ──────────────────────────────────────────────────────────

        /// <summary>
        /// Apply a UserEdit immutably and return the resulting new snapshot.
        /// The snapshot is automatically appended to History.
        /// ConflictDetector and DependencyResolver run after each edit.
        /// </summary>
        PlanSnapshot ApplyEdit(UserEdit edit);

        // ── Undo / Redo ───────────────────────────────────────────────────────

        bool CanUndo { get; }
        bool CanRedo { get; }

        /// <summary>
        /// Step back one snapshot in history and return it.
        /// Throws InvalidOperationException if CanUndo is false.
        /// </summary>
        PlanSnapshot Undo();

        /// <summary>
        /// Step forward one snapshot in history and return it.
        /// Throws InvalidOperationException if CanRedo is false.
        /// </summary>
        PlanSnapshot Redo();

        // ── Finalization ──────────────────────────────────────────────────────

        /// <summary>
        /// Run PlanValidator on the current snapshot and return a FinalizedPlan.
        /// Throws PlanValidationException if there are unresolved conflicts
        /// or invalid source/destination paths.
        /// Low-confidence ops are flagged; caller must confirm them via
        /// ConfirmLowConfidenceOp before calling Finalize again.
        /// </summary>
        FinalizedPlan Finalize();

        /// <summary>
        /// Mark a low-confidence operation as user-confirmed so Finalize()
        /// will accept it.
        /// </summary>
        void ConfirmLowConfidenceOp(Guid operationId);

        // ── Persistence ───────────────────────────────────────────────────────

        /// <summary>
        /// Serialize the FinalizedPlan to JSON at the given path.
        /// Returns the path written.
        /// </summary>
        string SaveFinalizedPlan(FinalizedPlan plan, string outputPath);

        /// <summary>
        /// Load a previously saved FinalizedPlan from JSON.
        /// Useful for auditing or resuming.
        /// </summary>
        FinalizedPlan LoadFinalizedPlan(string path);
    }
}
