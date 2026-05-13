namespace SDMS.Domain.Models;

public class MoveRecommendation
{
    public string FileName              { get; set; } = "";
    public string FullPath              { get; set; } = "";

    /// <summary>The immediate parent folder name this file currently lives in.</summary>
    public string CurrentFolder         { get; set; } = "";

    /// <summary>Display name of the recommended destination folder.</summary>
    public string RecommendedFolder     { get; set; } = "";

    /// <summary>
    /// Full path of the recommended destination.
    /// If the recommendation is a new folder that does not exist yet,
    /// this will be empty and RecommendedFolder will hold the suggested name.
    /// </summary>
    public string RecommendedFullPath   { get; set; } = "";

    /// <summary>0–100 confidence score.</summary>
    public int    Probability           { get; set; }

    /// <summary>Depth relative to the scan root (root = 0).</summary>
    public int    Depth                 { get; set; }
}