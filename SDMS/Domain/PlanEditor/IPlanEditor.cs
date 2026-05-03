using System.Buffers;
using SDMS.Domain.Models;

namespace SDMS.Domain.PlanEditor;

/// <summary>
/// Result of validating a plan or a single operation edit.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();

    public static ValidationResult Ok() => new() { IsValid = true };

    public static ValidationResult Fail(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors.ToList()
    };
}

/// <summary>
/// Represents an immutable snapshot of the plan at a point in time.
/// Used for undo/redo history.
/// </summary>
public class PlanSnapshot
{
    public List<PlannedOperation> Operations { get; init; } = new();
    public DateTime SnapshotAt { get; init; }
    public string Description { get; init; } = string.Empty; // e.g. "Deleted operation: Move Downloads/report.pdf"
}

/// <summary>
/// Allows the user to inspect and modify a ProposedPlan before finalization.
/// Maintains an undo/redo stack via immutable snapshots.
/// </summary>
public interface IPlanEditor
{
    /// <summary>Current working list of operations.</summary>
    IReadOnlyList<PlannedOperation> CurrentOperations { get; }

    bool CanUndo { get; }
    bool CanRedo { get; }

    // --- Editing ---

    /// <summary>Replace an existing operation by ID.</summary>
    ValidationResult UpdateOperation(Guid operationId, PlannedOperation updated);

    /// <summary>Add a user-defined operation to the plan.</summary>
    ValidationResult AddOperation(PlannedOperation operation);

    /// <summary>Remove an operation from the plan by ID.</summary>
    ValidationResult RemoveOperation(Guid operationId);

    /// <summary>Change the status of an operation (Approve, Skip, etc.).</summary>
    ValidationResult SetStatus(Guid operationId, OperationStatus status);

    // --- Undo/Redo ---

    void Undo();
    void Redo();

    // --- Validation ---

    /// <summary>
    /// Validates the entire current plan for conflicts and dependency issues.
    /// Run before calling Finalize().
    /// </summary>
    ValidationResult ValidatePlan();

    // --- Finalization ---

    /// <summary>
    /// Produces a FinalizedPlan from the current approved operations.
    /// Only operations with Status == Approved are included.
    /// Throws InvalidOperationException if ValidatePlan() returns errors.
    /// </summary>
    FinalizedPlan Finalize(ProposedPlan originalPlan);
}
