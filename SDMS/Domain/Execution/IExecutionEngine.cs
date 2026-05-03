using SDMS.Domain.Models;
using System.IO;

namespace SDMS.Domain.Execution;

public enum ExecutionState
{
    Idle,
    Preflight,
    Executing,
    Completed,
    PartialFailure,
    RollingBack,
    RolledBack
}

/// <summary>
/// Result of a single operation's execution attempt.
/// </summary>
public class OperationResult
{
    public Guid OperationId { get; init; }
    public OperationType OperationType { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime ExecutedAt { get; init; }

    /// <summary>Path used to stage deleted files. Null for non-delete operations.</summary>
    public string? StagingPath { get; init; }
}

/// <summary>
/// Full log produced after execution completes or fails.
/// </summary>
public class ExecutionLog
{
    public Guid PlanId { get; init; }
    public ExecutionState FinalState { get; init; }
    public List<OperationResult> Results { get; init; } = new();
    public DateTime StartedAt { get; init; }
    public DateTime EndedAt { get; init; }

    public int SuccessCount => Results.Count(r => r.Success);
    public int FailureCount => Results.Count(r => !r.Success);

    /// <summary>True if all operations succeeded.</summary>
    public bool IsFullSuccess => FinalState == ExecutionState.Completed && FailureCount == 0;
}

/// <summary>
/// Options controlling execution behavior.
/// </summary>
public class ExecutionOptions
{
    /// <summary>
    /// If true, deleted files are moved to a staging area instead of permanently deleted.
    /// Default true. Should almost never be false.
    /// </summary>
    public bool UseStagingForDeletes { get; init; } = true;

    /// <summary>Absolute path to the staging area for soft-deleted files.</summary>
    public string StagingAreaPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SDMS", "Staging");

    /// <summary>
    /// If true, the engine will attempt to roll back completed operations
    /// when a failure occurs mid-execution.
    /// </summary>
    public bool RollbackOnFailure { get; init; } = true;

    /// <summary>Dry run mode — validate and log but do not touch the filesystem.</summary>
    public bool DryRun { get; init; } = false;
}

/// <summary>
/// Safely applies a FinalizedPlan to the filesystem.
/// Supports preflight validation, staging, rollback, and full execution logging.
/// </summary>
public interface IExecutionEngine
{
    /// <summary>Current execution state. Useful for UI binding.</summary>
    ExecutionState State { get; }

    /// <summary>
    /// Validates the plan without executing it. Checks path existence,
    /// permission access, and operation ordering.
    /// </summary>
    Task<ValidationResult> PreflightAsync(
        FinalizedPlan plan,
        CancellationToken ct = default);

    /// <summary>
    /// Executes the FinalizedPlan against the filesystem.
    /// Runs preflight first. Throws InvalidOperationException if preflight fails
    /// and <paramref name="options"/> does not have DryRun enabled.
    /// </summary>
    /// <param name="plan">The finalized, user-approved plan.</param>
    /// <param name="options">Execution options.</param>
    /// <param name="progress">Reports (operationsCompleted, totalOperations, currentOperation).</param>
    /// <param name="ct">Cancellation token. Triggers rollback if RollbackOnFailure is true.</param>
    Task<ExecutionLog> ExecuteAsync(
        FinalizedPlan plan,
        ExecutionOptions? options = null,
        IProgress<(int Completed, int Total, PlannedOperation Current)>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Rolls back all operations recorded in the provided ExecutionLog.
    /// Only valid if State is PartialFailure or Completed.
    /// </summary>
    Task<ExecutionLog> RollbackAsync(
        ExecutionLog log,
        CancellationToken ct = default);
}

// Re-export ValidationResult so callers don't need two using statements
public class ValidationResult : PlanEditor.ValidationResult { }
