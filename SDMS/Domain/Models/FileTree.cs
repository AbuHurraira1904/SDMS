namespace SDMS.Domain.Models;

public class FileTree
{
    public FileNode Root { get; init; }
    public DateTime ScannedAt { get; init; }
    public string ScanRootPath { get; init; }
    public int TotalFiles { get; init; }
    public int TotalDirectories { get; init; }
    public long TotalSizeBytes { get; init; }
    public List<string> SkippedPaths { get; init; }  // Permission denied, etc.
}