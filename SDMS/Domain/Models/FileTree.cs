namespace SDMS.Domain.Models;

public sealed record FileTree
{
    public required FileNode Root { get; init; }
    public required DateTime ScannedAt { get; init; }
    public required string ScanRootPath { get; init; }
    
    public required int TotalFiles { get; init; }
    public required int TotalDirectories { get; init; }
    public required long TotalSizeBytes { get; init; }
    public required int TotalIgnoredFiles { get; init; }
    
    public required List<string> SkippedPaths { get; init; }
}