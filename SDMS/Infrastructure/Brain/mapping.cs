using SDMS.Domain.Brain;
using SDMS.Domain.Models;

namespace SDMS.Infrastructure.Brain;

public static class PlanMappingExtensions
{
    /// <summary>
    /// Converts the raw HTTP response DTO into the domain model
    /// that ExecutionEngine and the UI actually work with.
    /// </summary>
    public static FinalizedPlan ToFinalizedPlan(this PlanOutput planOutput)
    {
        var operations = planOutput.Operations
            .Select(op => op.ToPlannedOperation())
            .ToList();

        return new FinalizedPlan
        {
            Id         = Guid.NewGuid(),
            Operations = operations,
            FinalizedAt = DateTime.UtcNow,
            // OriginalPlan and SourcePlanId are set by the caller
            // once the user has reviewed and confirmed the plan.
        };
    }

    public static PlannedOperation ToPlannedOperation(this PlanOp op)
    {
        if (!Enum.TryParse<OpType>(op.OpType, ignoreCase: true, out var opType))
            throw new InvalidOperationException(
                $"Unknown OpType from brain: '{op.OpType}'. " +
                $"Valid values: {string.Join(", ", Enum.GetNames<OpType>())}");

        if (!Enum.TryParse<OpStatus>(op.Status, ignoreCase: true, out var status))
            status = OpStatus.Pending; // safe default — brain always sends "Pending"

        return new PlannedOperation
        {
            Id              = Guid.NewGuid(),
            Type            = opType,
            SourcePath      = op.Source,
            DestinationPath = op.Destination,
            Reason          = op.Reason,
            Confidence      = op.Confidence,
            Importance      = op.Importance,
            Status          = status,
            IsUserAdded     = false,   // everything from the brain is brain-generated
        };
    }
}