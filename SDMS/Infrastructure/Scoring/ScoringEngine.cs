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
        var now   = DateTime.UtcNow;
        var files = report.SourceTree.Root.Children;

         
        var hashCount = files  
            .Where(f => f.Hash is not null)
            .GroupBy(f => f.Hash!)
            .ToDictionary(g => g.Key, g => g.Count());

        var items = new List<(Models.FileNode Node, Dictionary<string, double> Breakdown, double Raw)>();

        foreach (var file in files)
        {
            
            double recency  = 10.0 * Math.Exp(-0.05 * Math.Max(0, (now - file.AccessedAt).TotalDays));
            double modified = 10.0 * Math.Exp(-0.05 * Math.Max(0, (now - file.ModifiedAt).TotalDays));
            double type     = weights.FileTypePriorityMap.TryGetValue(file.Extension, out int v) ? Math.Clamp(v, 0, 10) : 5.0;
            double size     = file.SizeBytes > 0
                                ? Math.Clamp(10.0 * Math.Exp(-0.5 * Math.Pow((Math.Log10(file.SizeBytes) - 6.0) / 2.0, 2))
                                             * (file.SizeBytes >= 500L * 1024 * 1024 ? 0.3 : 1.0), 0, 10)
                                : 0.0;

            double dupPen = file.Hash is not null && hashCount.GetValueOrDefault(file.Hash) > 1 ? 10.0 : 0.0;
            double sysPen = file.Attributes.HasFlag(FileAttributes.System) ||
                            file.Attributes.HasFlag(FileAttributes.Hidden) ? 10.0 : 0.0;

            double raw = Math.Max(0.0,
                  weights.RecencyWeight          * recency
                + weights.ModifiedRecencyWeight  * modified
                + weights.FileTypePriorityWeight * type
                + weights.LargeSizePenaltyWeight * size
                - weights.DuplicatePenaltyWeight  * dupPen
                - weights.SystemFilePenaltyWeight * sysPen);

            items.Add((file, new Dictionary<string, double>
            {
                ["recency"]  = recency ,  ["modified"] = modified,
                ["type"]     = type    ,     ["size"]     = size,
                ["dup_pen"]  = dupPen  ,   ["sys_pen"]  = sysPen,
                ["raw"]      = raw,
            }, raw));
        }

        double min = items.Min(x => x.Raw), max = items.Max(x => x.Raw);
        double range = max - min;

        var result = new List<Models.ScoredFileNode>(items.Count);
        foreach (var x in items)
        {
            int score = (int)Math.Round(Math.Clamp(range < 1e-9 ? 50.0 : (x.Raw - min) / range * 100.0, 0, 100));
            x.Breakdown["score"] = score;
            result.Add(new Models.ScoredFileNode { Node = x.Node, Score = score, ScoreBreakdown = x.Breakdown });
        }

        return Task.FromResult(result);
    }
}

