namespace SDMS.Domain.PlanEditor
{
    /// <summary>
    /// Discriminated union of all edit events the UI can send to the Plan Editor.
    /// Each edit produces a new immutable plan snapshot (supports undo/redo).
    /// </summary>
    public abstract class UserEdit
    {
        /// <summary>UTC timestamp of this edit (for audit log).</summary>
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    /// <summary>Remove an operation entirely from the plan.</summary>
    public class RemoveOperationEdit : UserEdit
    {
        /// <summary>Id of the PlanOperation to remove.</summary>
        public Guid OperationId { get; init; }
    }

    /// <summary>Change the destination path of an existing Move operation.</summary>
    public class ChangeDestinationEdit : UserEdit
    {
        public Guid OperationId { get; init; }

        /// <summary>New absolute destination path.</summary>
        public string NewDestination { get; init; } = string.Empty;
    }

    /// <summary>Manually add a new Move operation.</summary>
    public class AddMoveEdit : UserEdit
    {
        public string Source      { get; init; } = string.Empty;
        public string Destination { get; init; } = string.Empty;

        /// <summary>Optional user-provided reason shown in the UI.</summary>
        public string Reason      { get; init; } = "Manual move";
    }

    /// <summary>Manually add a NewFolder operation.</summary>
    public class AddNewFolderEdit : UserEdit
    {
        /// <summary>Absolute path of the folder to create.</summary>
        public string FolderPath { get; init; } = string.Empty;
        public string Reason     { get; init; } = "Manual folder";
    }

    /// <summary>
    /// Rescue a file: change an existing Delete operation into a Move.
    /// The user must supply a destination.
    /// </summary>
    public class RescueFileEdit : UserEdit
    {
        public Guid   OperationId  { get; init; }

        /// <summary>Where to move the rescued file instead of deleting it.</summary>
        public string Destination  { get; init; } = string.Empty;
    }

    /// <summary>
    /// Reclassify an unclassified file by moving it to a specific target folder.
    /// Produces a Move op (or updates an existing one) with the given target folder.
    /// </summary>
    public class ReclassifyFileEdit : UserEdit
    {
        /// <summary>Absolute path of the file to reclassify.</summary>
        public string FilePath     { get; init; } = string.Empty;

        /// <summary>Absolute path of the target folder.</summary>
        public string TargetFolder { get; init; } = string.Empty;
        public string Reason       { get; init; } = "Manual reclassification";
    }
}
