using SDMS.Domain.Models;
using SDMS.Domain.PlanEditor;

namespace SDMS.Infrastructure.PlanEditor;

public sealed class PlanEditor : IPlanEditor
{
    private List<PlannedOperation> _currentOps = new();
    private readonly Stack<PlanSnapshot> _undoStack = new();
    private readonly Stack<PlanSnapshot> _redoStack = new();

    public IReadOnlyList<PlannedOperation> CurrentOperations => _currentOps.AsReadOnly();
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public PlanEditor(ProposedPlan initialPlan)
    {
        // Start with the AI's suggestions
        _currentOps = initialPlan.CloneOperations();
    }

    // --- State Management Helpers ---

    private void CreateSnapshot(string description)
    {
        _undoStack.Push(new PlanSnapshot 
        { 
            Operations = _currentOps.Select(op => op.Clone()).ToList(),
            SnapshotAt = DateTime.UtcNow,
            Description = description
        });
        _redoStack.Clear(); // New action invalidates redo history
    }

    // --- Editing Logic ---

    public ValidationResult UpdateOperation(Guid operationId, PlannedOperation updated)
    {
        var index = _currentOps.FindIndex(o => o.Id == operationId);
        if (index == -1) return ValidationResult.Fail("Operation not found.");

        CreateSnapshot($"Updated operation: {operationId}");
        _currentOps[index] = updated;
        return ValidationResult.Ok();
    }

    public ValidationResult AddOperation(PlannedOperation operation)
    {
        CreateSnapshot($"Added manual operation: {operation.Type}");
        _currentOps.Add(operation);
        return ValidationResult.Ok();
    }

    public ValidationResult RemoveOperation(Guid operationId)
    {
        var op = _currentOps.FirstOrDefault(o => o.Id == operationId);
        if (op == null) return ValidationResult.Fail("Operation not found.");

        CreateSnapshot($"Removed operation: {op.SourcePath}");
        _currentOps.RemoveAll(o => o.Id == operationId);
        return ValidationResult.Ok();
    }

    public ValidationResult SetStatus(Guid operationId, OpStatus status)
    {
        var op = _currentOps.FirstOrDefault(o => o.Id == operationId);
        if (op == null) return ValidationResult.Fail("Operation not found.");

        CreateSnapshot($"Set status to {status} for {op.Id}");
        op.Status = status; // Enums are value types, but the list holds the ref
        return ValidationResult.Ok();
    }

    // --- Undo / Redo ---

    public void Undo()
    {
        if (!CanUndo) return;
        
        // Save current to redo
        _redoStack.Push(new PlanSnapshot { Operations = _currentOps.Select(o => o.Clone()).ToList() });
        
        var snapshot = _undoStack.Pop();
        _currentOps = snapshot.Operations;
    }

    public void Redo()
    {
        if (!CanRedo) return;

        // Save current to undo
        _undoStack.Push(new PlanSnapshot { Operations = _currentOps.Select(o => o.Clone()).ToList() });

        var snapshot = _redoStack.Pop();
        _currentOps = snapshot.Operations;
    }

    // --- Validation Logic ---

    public ValidationResult ValidatePlan()
    {
        var errors = new List<string>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var op in _currentOps.Where(o => o.Status != OpStatus.Skipped))
        {
            // 1. Conflict Detection: Duplicate Destinations
            if (!string.IsNullOrEmpty(op.DestinationPath))
            {
                if (!destinations.Add(op.DestinationPath))
                    errors.Add($"Collision: Multiple files moving to {op.DestinationPath}");
            }

            // 2. Structural Check
            if (op.Type == OpType.Move && string.IsNullOrEmpty(op.SourcePath))
                errors.Add($"Invalid Move: Source path missing for operation {op.Id}");
        }

        return errors.Count > 0 ? new ValidationResult { IsValid = false, Errors = errors } : ValidationResult.Ok();
    }

    // --- Finalization ---

    public FinalizedPlan Finalize(ProposedPlan originalPlan)
    {
        var validation = ValidatePlan();
        if (!validation.IsValid)
            throw new InvalidOperationException("Cannot finalize plan with errors: " + string.Join(", ", validation.Errors));

        // Filter for only Approved/Confirmed operations
        var orderedOps = _currentOps
            .Where(o => o.Status == OpStatus.Confirmed || o.Status == OpStatus.Pending)
            .OrderBy(o => GetPriority(o.Type))
            .Select(o => o.Clone())
            .ToList();

        return new FinalizedPlan
        {
            SourcePlanId = originalPlan.Id,
            OriginalPlan = originalPlan,
            Operations = orderedOps,
            FinalizedAt = DateTime.UtcNow
        };
    }
    
    private int GetPriority(OpType type) => type switch
    {
        OpType.NewFolder => 1,     // Create infrastructure first
        OpType.RenameFolder => 2,
        OpType.Move => 3,          // Move files into infrastructure
        OpType.Delete => 4,        // Cleanup files
        OpType.DeleteFolder => 5,  // Cleanup empty folders last
        _ => 99
    };
}