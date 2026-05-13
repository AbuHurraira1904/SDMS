using SDMS.Domain.Models;
using SDMS.Domain.Analysis;

namespace SDMS.Infrastructure.Analysis;

public sealed class AnalysisEngine : IAnalysisEngine
{
    public async Task<AnalysisReport> AnalyzeAsync(FileTree tree, CancellationToken ct = default)
    {
        // Pure computation: we use Task.Run to ensure the UI stays responsive 
        // during the tree traversal, even though there's no IO.
        return await Task.Run(() =>
        {
            var metrics = tree.BasicInfo;
            
            var report = new AnalysisReport
            {
                SourceTree = tree,
                AnalyzedAt = DateTime.UtcNow,
                
                // 1. Direct mapping from FolderAnalysisMetrics
                FileTypeDistribution = new Dictionary<string, int>(metrics.ExtensionCounts),
                FileTypeSizeMap = new Dictionary<string, long>(metrics.ExtensionSizes),
                
                // 2. Collections of Object References (System/Ignored)
                IgnoredFiles = new List<FileNode>(),
                SystemFiles = new List<FileNode>(),
                
                // 3. Heuristic labeling
                RequiredLabels = GenerateLabels(metrics)
            };

            // 4. Recursive traversal to link FileNode references
            PopulateFileCollections(tree.Root, report, ct);

            return report;
        }, ct);
    }

    private void PopulateFileCollections(FileNode node, AnalysisReport report, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Check node flags populated by the Scanner
        if (node.IsSystem) report.SystemFiles.Add(node);
        if (node.IsHidden) report.IgnoredFiles.Add(node);

        foreach (var child in node.Children)
        {
            PopulateFileCollections(child, report, ct);
        }
    }

    private List<string> GenerateLabels(FolderAnalysisMetrics metrics)
    {
        // Identify "dominant" types to suggest folders
        return metrics.ExtensionCounts
            .Where(kvp => kvp.Value > 2) 
            .Select(kvp => kvp.Key.TrimStart('.').ToUpper() + "s")
            .Distinct()
            .ToList();
    }
}