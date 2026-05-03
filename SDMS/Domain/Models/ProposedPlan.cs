namespace SDMS.Domain.Models;

public class ProposedPlan
{
    public List<PlannedOperation> Operations { get; init; }
    public AnalysisReport SourceReport { get; init; }
    public DateTime GeneratedAt { get; init; }
}