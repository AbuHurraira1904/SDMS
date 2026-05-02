namespace SDMS.SDMS.Domain.Models;

public class ProposedPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public List<PlannedOperation> Operations { get; set; } = new();
    public AnalysisReport SourceReport { get; init; }
    public DateTime GeneratedAt { get; init; }
    public int OperationCount => Operations.Count;

    /// Deep copy of the operations list for snapshot creation
    public List<PlannedOperation> CloneOperations()
        => Operations.Select(op => op.Clone()).ToList();
}

