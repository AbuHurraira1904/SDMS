namespace SDMS.Domain.Models;

public sealed record FileTree
{
    public required FileNode Root { get; init; }
    public required DateTime ScannedAt { get; init; }
    public required string ScanRootPath { get; init; }
    
    // this stores the most basic analysis
    public required FolderAnalysisMetrics BasicInfo { get; init; }
}