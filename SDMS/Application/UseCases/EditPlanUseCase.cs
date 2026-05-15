// ============================================================
// EditPlanUseCase.cs  →  SDMS.Application/UseCases/
// Phase 5 — User review and plan editing.
//
// This use case owns the IPlanEditor instance for one session.
// The UI calls methods on this class rather than touching
// IPlanEditor directly, keeping the Application layer as the
// only layer the UI communicates with.
// ============================================================

using SDMS.Application.DTOs;
using SDMS.Domain.Models;
using SDMS.Domain.PlanEditor;

namespace SDMS.Application.UseCases;

public sealed class EditPlanUseCase
{
    private readonly IPlanEditor _editor;
    private readonly ProposedPlan _originalPlan;

    public EditPlanUseCase(IPlanEditor editor, ProposedPlan originalPlan)
    {
        _editor       = editor;
        _originalPlan = originalPlan;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public IReadOnlyList<PlannedOperation> CurrentOperations => _editor.CurrentOperations;

    public bool CanUndo => _editor.CanUndo;
    public bool CanRedo => _editor.CanRedo;

    // ── Write ─────────────────────────────────────────────────────────────────

    public ValidationResult ApproveOperation(Guid id)
        => _editor.SetStatus(id, OpStatus.Confirmed);

    public ValidationResult SkipOperation(Guid id)
        => _editor.SetStatus(id, OpStatus.Skipped);

    public ValidationResult ChangeDestination(Guid id, string newDestination)
    {
        var op = _editor.CurrentOperations.FirstOrDefault(o => o.Id == id);
        if (op is null)
            return ValidationResult.Fail($"Operation {id} not found.");

        var updated = op.Clone();
        updated.DestinationPath = newDestination;
        return _editor.UpdateOperation(id, updated);
    }

    public ValidationResult AddManualOperation(PlannedOperation operation)
        => _editor.AddOperation(operation);

    public ValidationResult RemoveOperation(Guid id)
        => _editor.RemoveOperation(id);

    public void Undo() => _editor.Undo();
    public void Redo() => _editor.Redo();

    // ── Validation & Finalization ─────────────────────────────────────────────

    /// <summary>
    /// Validates the current plan. Call this before showing the Execute button.
    /// </summary>
    public ValidationResult Validate() => _editor.ValidatePlan();

    /// <summary>
    /// Finalizes the plan for execution.
    /// Throws <see cref="InvalidOperationException"/> if validation fails.
    /// </summary>
    public FinalizedPlan Finalize()
    {
        var validation = _editor.ValidatePlan();
        if (!validation.IsValid)
            throw new InvalidOperationException(
                "Cannot finalize plan with validation errors: " +
                string.Join("; ", validation.Errors));

        return _editor.Finalize(_originalPlan);
    }
}
