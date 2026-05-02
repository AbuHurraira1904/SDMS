using SDMS.Domain.Models;

namespace SDMS.Domain.PlanEditor
{

    public class PlanSnapshot
    {
        /// <summary>Sequential index in the edit history (0 = original plan).</summary>
        public int SnapshotIndex { get; init; }

        /// <summary>UTC timestamp when this snapshot was created.</summary>
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// The edit that produced this snapshot.
        /// Null for the initial snapshot (index 0).
        /// </summary>
        public UserEdit? ProducedBy { get; init; }

        /// <summary>
        /// Immutable copy of the operations at this point in history.
        /// Never mutate this list — always clone before applying new edits.
        /// </summary>
        public IReadOnlyList<PlanOperation> Operations { get; init; }
            = Array.Empty<PlanOperation>();

        /// <summary>
        /// Conflicts detected at this snapshot (empty = clean).
        /// </summary>
        public IReadOnlyList<PlanConflict> Conflicts { get; init; }
            = Array.Empty<PlanConflict>();

        /// <summary>True when there are no unresolved conflicts.</summary>
        public bool IsClean => !Conflicts.Any();
    }

    /// <summary>
    /// A conflict detected by ConflictDetector after an edit is applied.
    /// </summary>
    public class PlanConflict
    {
        public ConflictType Type        { get; init; }
        public string       Description { get; init; } = string.Empty;

        /// <summary>IDs of the operations involved in this conflict.</summary>
        public List<Guid>   OperationIds { get; init; } = new();
    }

    public enum ConflictType
    {
        /// <summary>Two or more ops write to the exact same destination path.</summary>
        DuplicateDestination,

        /// <summary>A Move targets a folder that has no NewFolder op creating it.</summary>
        MissingTargetFolder,

        /// <summary>A DeleteFolder op targets a folder that still has pending Moves into it.</summary>
        DeleteFolderWithPendingMoves,

        /// <summary>A source path does not exist on disk.</summary>
        InvalidSourcePath,

        /// <summary>A destination path is syntactically invalid.</summary>
        InvalidDestinationPath
    }
}
