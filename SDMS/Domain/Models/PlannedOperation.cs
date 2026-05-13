namespace SDMS.Domain.Models;

public enum OpType
{
    Move,
    Delete,
    NewFolder,
    DeleteFolder,
    RenameFolder,
    MoveFolder,
    MergeFolder
}

public enum OpStatus
{
    Pending,
    Confirmed,      // user explicitly approved
    Skipped,        // user removed it
    Executed,       // execution engine completed it
    Failed          // execution engine failed it
}


public class PlannedOperation
{
    public Guid Id { get; init; }
    public OpType Type { get; init; }
    public string? SourcePath { get; init; }
    public string? DestinationPath { get; set; }
    public string Reason { get; init; }           // Explainability
    public double Confidence { get; init; }        // 0.0–1.0
    public int Importance { get; init; }           // For UI sorting
    public bool IsUserAdded { get; init; }         // Manual vs Brain-generated
    public bool IsDirectoryOp => Type switch
    {
        OpType.NewFolder or OpType.DeleteFolder or 
            OpType.RenameFolder or OpType.MoveFolder or 
            OpType.MergeFolder => true,
        _ => false // Move and Delete are handled by runtime check
    };
    public OpStatus Status { get; set; }
    
    public PlannedOperation Clone() => new PlannedOperation
    {
        Id          = this.Id,
        Type      = this.Type,
        SourcePath      = this.SourcePath,
        DestinationPath = this.DestinationPath,
        Reason      = this.Reason,
        Confidence  = this.Confidence,
        Importance  = this.Importance,
        Status      = this.Status,
        IsUserAdded    = this.IsUserAdded
    };
}