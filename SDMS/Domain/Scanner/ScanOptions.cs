namespace SDMS.Domain.Scanner;

public sealed record ScanOptions
{
    public bool FollowSymlinks { get; init; } = false;
    public bool IncludeHidden { get; init; } = false;
    public bool IncludeSystemFiles { get; init; } = false;

    // Common noise directories excluded by default
    public List<string> ExcludedDirectoryNames { get; init; } =
    [
        "node_modules", ".git", ".vs", ".idea", "bin", "obj", 
        "$RECYCLE.BIN", "System Volume Information"
    ];

    public List<string> ExcludedExtensions { get; init; } = [];

    public int? MaxDepth { get; init; } = null;
    public long? MaxFileSizeBytes { get; init; } = null;
}