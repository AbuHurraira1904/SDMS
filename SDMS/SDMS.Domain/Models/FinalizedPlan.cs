namespace SDMS.SDMS.Domain.Models;

public class FinalizedPlan
{
    public Guid Id { get; init; } = Guid.NewGuid(); 
    public Guid SourcePlanId { get; set; }              // id of proposed plan
    public List<PlannedOperation> Operations { get; init; }
    public ProposedPlan OriginalPlan { get; init; }
    public DateTime FinalizedAt { get; init; }
    public int OperationCount => Operations.Count;
    // public List<Guid> LowConfidenceConfirmed { get; set; } = new();
}

