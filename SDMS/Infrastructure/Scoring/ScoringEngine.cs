using SDMS.Domain.Scoring;
using Models = SDMS.Domain.Models;
using System.IO;
using System;
namespace SDMS.Infrastructure.Scoring;

public sealed class ScoringEngine : IScoringEngine
{
    public Task<List<Models.ScoredFileNode>> ScoreAsync(
        Models.AnalysisReport report,
        ScoringWeights? weights = null,
        CancellationToken ct = default)
    {
        weights ??= ScoringWeights.Default;
        var now = DateTime.UtcNow;

        // FIX 1: Get ALL files from the tree, not just the root children
        var allNodes = Flatten(report.SourceTree.Root).Where(n => !n.IsDirectory).ToList();

        // FIX 2: Handle Hashing gracefully
        var hashCount = allNodes
            .Where(f => !string.IsNullOrEmpty(f.Hash))
            .GroupBy(f => f.Hash!)
            .ToDictionary(g => g.Key, g => g.Count());

        var items = new List<(Models.FileNode Node, Dictionary<string, double> Breakdown, double Raw)>();

        foreach (var file in allNodes)
        {
            ct.ThrowIfCancellationRequested();

            // Calculate base signals (Friend's logic was great here)
            double recency = 10.0 * Math.Exp(-0.05 * Math.Max(0, (now - file.AccessedAt).TotalDays));
            double modified = 10.0 * Math.Exp(-0.05 * Math.Max(0, (now - file.ModifiedAt).TotalDays));
            
            // Extension lookup
            double type = weights.FileTypePriorityMap.TryGetValue(file.Extension.ToLower(), out int v) 
                          ? Math.Clamp(v, 0, 10) : 5.0;

            // Size Bell Curve (Sweet spot around 1MB)
            double size = file.SizeBytes > 0
                ? Math.Clamp(10.0 * Math.Exp(-0.5 * Math.Pow((Math.Log10(file.SizeBytes) - 6.0) / 2.0, 2)), 0, 10)
                : 0.0;
            
            // Apply Penalty for massive files
            if (file.SizeBytes >= 500L * 1024 * 1024) size *= 0.3;

            double dupPen = !string.IsNullOrEmpty(file.Hash) && hashCount.GetValueOrDefault(file.Hash) > 1 ? 10.0 : 0.0;
            
            // Use Attributes from the FileNode
            double sysPen = (file.IsSystem || file.IsHidden) ? 10.0 : 0.0;

            // Weighted Fusion
            double raw = Math.Max(0.0,
                  (weights.RecencyWeight * recency)
                + (weights.ModifiedRecencyWeight * modified)
                + (weights.FileTypePriorityWeight * type)
                + (weights.LargeSizePenaltyWeight * size)
                - (weights.DuplicatePenaltyWeight * dupPen)
                - (weights.SystemFilePenaltyWeight * sysPen));

            items.Add((file, new Dictionary<string, double>
            {
                ["recency"] = recency,
                ["modified"] = modified,
                ["type"] = type,
                ["size"] = size,
                ["raw"] = raw
            }, raw));
        }

        // Normalization
        if (items.Count == 0) return Task.FromResult(new List<Models.ScoredFileNode>());

        double min = items.Min(x => x.Raw);
        double max = items.Max(x => x.Raw);
        double range = max - min;

        var result = items.Select(x => new Models.ScoredFileNode
        {
            Node = x.Node,
            Score = (int)Math.Round(Math.Clamp(range < 1e-9 ? 50.0 : (x.Raw - min) / range * 100.0, 0, 100)),
            ScoreBreakdown = x.Breakdown
        }).ToList();

        return Task.FromResult(result);
    }

    private IEnumerable<Models.FileNode> Flatten(Models.FileNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }
}

