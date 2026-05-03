namespace SDMS.Domain.Models;

public class ScoredFileNode
{
    public FileNode Node { get; init; }
    public int Score { get; init; }                              // 0–100
    public Dictionary<string, double> ScoreBreakdown { get; init; } // factor → contribution
}