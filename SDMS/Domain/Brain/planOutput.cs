namespace SDMS.Domain.Brain;

public sealed record PlanOutput
{
    public required string          ScanRoot           { get; init; }
    public required int             TotalFilesScanned  { get; init; }
    public required SafetySummary   Safety             { get; init; }
    public required List<PlanOp>    Operations         { get; init; }
}

public sealed record SafetySummary
{
    public required int             TotalOperations    { get; init; }
    public required int             SafeOperations     { get; init; }
    public required int             BlockedOperations  { get; init; }
    public required int             AffectedFiles      { get; init; }
}

public sealed record PlanOp
{
    public required string   OpType      { get; init; }   // Move | NewFolder | DeleteFolder | MoveFolder
    public required string   Source      { get; init; }
    public          string?  Destination { get; init; }
    public required string   Reason      { get; init; }
    public required double   Confidence  { get; init; }
    public required double   Importance  { get; init; }
    public required string   Status      { get; init; }   // always "Pending" from Python
}