namespace SDMS.Domain.Models;

public enum OperationType { Move, Delete, CreateFolder, Rename, Merge }

public class PlannedOperation
{
    public Guid Id { get; init; }
    public OperationType Type { get; init; }
    public string? SourcePath { get; init; }
    public string? DestinationPath { get; init; }
    public string Reason { get; init; }           // Explainability
    public double Confidence { get; init; }        // 0.0–1.0
    public int Importance { get; init; }           // For UI sorting
    public bool IsUserAdded { get; init; }         // Manual vs Brain-generated
}