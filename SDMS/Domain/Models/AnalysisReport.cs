namespace SDMS.SDMS.Domain.Models;

public class AnalysisReport
{
    public FileTree SourceTree { get; init; }
    public Dictionary<string, int> FileTypeDistribution { get; init; }  // ext → count
    public Dictionary<string, long> FileTypeSizeMap { get; init; }       // ext → bytes
    public List<FileNode> IgnoredFiles { get; init; }
    public List<FileNode> SystemFiles { get; init; }
    public List<string> RequiredLabels { get; init; }  // Preliminary folder labels
    public DateTime AnalyzedAt { get; init; }
}