namespace SDMS.Domain.Models;


public record SubDirSummary(string Name, string Path, long Size);


public class FolderAnalysisMetrics
{
    // { "Images": 45, "Video": 12 }
    public Dictionary<string, int> CategoryCounts { get; } = new();
    public Dictionary<string, int> ExtensionCounts { get; } = new();
    
    // { "Images": 4500000 }
    public Dictionary<string, long> CategorySizes { get; } = new();
    
    // Detailed breakdown for the Top 10 extensions
    public Dictionary<string, long> ExtensionSizes { get; } = new();

    public List<string> HiddenPaths { get; } = [];
    public List<string> SystemPaths { get; } = [];
    public List<string> SkippedPaths { get; } = [];

    public DateTime OldestFile { get; set; } = DateTime.MaxValue;
    public DateTime NewestFile { get; set; } = DateTime.MinValue;

    // Sub-directory summaries for the "Root" level only
    public List<SubDirSummary> ImmediateSubDirs { get; } = [];
        
    // basic info
    public int TotalFiles { get; set; } = 0;
    public int TotalDirectories { get; set; } = 0;
    public long TotalSizeBytes { get; set; } = 0;
    public int TotalIgnoredFiles { get; set; } = 0;
    
    
    public FolderAnalysisMetrics DeepCopy()
    {
        var clone = new FolderAnalysisMetrics
        {
            // Copy Value Types
            OldestFile = this.OldestFile,
            NewestFile = this.NewestFile,
            TotalFiles = this.TotalFiles,
            TotalDirectories = this.TotalDirectories,
            TotalSizeBytes = this.TotalSizeBytes,
            TotalIgnoredFiles = this.TotalIgnoredFiles
        };

        // Copy Dictionaries
        foreach (var kvp in this.CategoryCounts) clone.CategoryCounts.Add(kvp.Key, kvp.Value);
        foreach (var kvp in this.ExtensionCounts) clone.ExtensionCounts.Add(kvp.Key, kvp.Value);
        foreach (var kvp in this.CategorySizes) clone.CategorySizes.Add(kvp.Key, kvp.Value);
        foreach (var kvp in this.ExtensionSizes) clone.ExtensionSizes.Add(kvp.Key, kvp.Value);

        // Copy Lists
        clone.HiddenPaths.AddRange(this.HiddenPaths);
        clone.SystemPaths.AddRange(this.SystemPaths);
        clone.SkippedPaths.AddRange(this.SkippedPaths);
        
        // SubDirSummary is a record (value-like behavior), so simple AddRange is safe
        clone.ImmediateSubDirs.AddRange(this.ImmediateSubDirs);

        return clone;
    }
}