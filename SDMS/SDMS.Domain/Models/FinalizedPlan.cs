namespace SDMS.SDMS.Domain.Models;

public class FinalizedPlan
{
    public List<PlannedOperation> Operations { get; init; }
    public ProposedPlan OriginalPlan { get; init; }
    public DateTime FinalizedAt { get; init; }
}