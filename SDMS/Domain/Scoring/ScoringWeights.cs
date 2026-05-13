namespace SDMS.Domain.Scoring;

/// <summary>
/// Configurable weights for the Scoring Engine.
/// All weights are relative. The engine normalizes them internally.
/// Loaded from scoring_weights.json at runtime.
/// </summary>
public class ScoringWeights
{
    /// <summary>How recently the file was accessed.</summary>
    public double RecencyWeight { get; init; } = 1.0;

    /// <summary>How recently the file was modified.</summary>
    public double ModifiedRecencyWeight { get; init; } = 1.0;

    /// <summary>Priority based on file extension category (e.g. documents > temp files).</summary>
    public double FileTypePriorityWeight { get; init; } = 1.0;

    /// <summary>Quality of the containing folder's name/structure.</summary>
    public double FolderContextWeight { get; init; } = 0.5;

    /// <summary>Penalty for very large files unlikely to be user-created content.</summary>
    public double LargeSizePenaltyWeight { get; init; } = 0.3;

    /// <summary>Penalty applied to suspected duplicates.</summary>
    public double DuplicatePenaltyWeight { get; init; } = 0.8;

    /// <summary>Penalty for system/hidden files that slipped through filtering.</summary>
    public double SystemFilePenaltyWeight { get; init; } = 1.0;

    /// <summary>
    /// File type priority overrides. Extension → priority score (0–10).
    /// e.g. { ".pdf": 8, ".tmp": 1 }
    /// </summary>
    /// <summary>Priority based on broad MIME category.</summary>
    public Dictionary<string, int> CategoryPriorityMap { get; init; } = new()
    {
        ["document"]   = 9,
        ["code"]       = 8,
        ["image"]      = 6,
        ["video"]      = 6,
        ["audio"]      = 5,
        ["archive"]    = 4,
        ["data"]       = 3,
        ["executable"] = 1,
        ["temp"]       = 0,
        ["system"]     = 0
    };

    public static ScoringWeights Default => new();
}
