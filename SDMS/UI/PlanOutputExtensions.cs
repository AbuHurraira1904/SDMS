using SDMS.Domain.Brain;
using SDMS.Domain.Models;

namespace SDMS.UI;

/// <summary>
/// Maps a Brain PlanOutput into the C# domain models (ProposedPlan / FinalizedPlan).
/// Lives in the UI layer because it bridges the Brain contract with the Domain model.
/// </summary>
public static class PlanOutputExtensions
{
    public static ProposedPlan ToProposedPlan(this PlanOutput output)
    {
        var ops = output.Operations
            .Select(op => new PlannedOperation
            {
                Id              = Guid.NewGuid(),
                Type            = ParseOpType(op.OpType),
                SourcePath      = op.Source,
                DestinationPath = op.Destination,
                Reason          = op.Reason,
                Confidence      = op.Confidence,
                Importance      = op.Importance,
                Status          = ParseStatus(op.Status),
                IsUserAdded     = false,
            })
            .ToList();

        return new ProposedPlan
        {
            Operations  = ops,
            GeneratedAt = DateTime.UtcNow,
        };
    }

    public static FinalizedPlan ToFinalizedPlan(this PlanOutput output)
    {
        var proposed = output.ToProposedPlan();
        return new FinalizedPlan
        {
            SourcePlanId = proposed.Id,
            OriginalPlan = proposed,
            Operations   = proposed.CloneOperations(),
            FinalizedAt  = DateTime.UtcNow,
        };
    }

    private static OpType ParseOpType(string raw) => raw.ToLowerInvariant() switch
    {
        "move"         => OpType.Move,
        "delete"       => OpType.Delete,
        "newfolder"    => OpType.NewFolder,
        "deletefolder" => OpType.DeleteFolder,
        "renamefolder" => OpType.RenameFolder,
        "movefolder"   => OpType.MoveFolder,
        "mergefolder"  => OpType.MergeFolder,
        _              => OpType.Move,
    };

    private static OpStatus ParseStatus(string raw) => raw.ToLowerInvariant() switch
    {
        "confirmed" => OpStatus.Confirmed,
        "skipped"   => OpStatus.Skipped,
        "executed"  => OpStatus.Executed,
        "failed"    => OpStatus.Failed,
        _           => OpStatus.Pending,
    };
}
