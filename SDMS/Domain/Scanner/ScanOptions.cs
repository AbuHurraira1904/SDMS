namespace SDMS.Domain.Scanner;

public sealed record ScanOptions
{
    public bool FollowSymlinks { get; init; } = false;
    public bool IncludeHidden { get; init; } = false;
    public bool IncludeSystemFiles { get; init; } = false;

    // Common noise directories excluded by default
    public List<string> ExcludedDirectoryNames { get; init; } =
    [
        // Windows OS internals
        "Windows",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "Recovery",
        "System Volume Information",
        "$RECYCLE.BIN",
        "$WinREAgent",
        "$Windows.~WS",
        "$Windows.~BT",
        "MSOCache",
        "OneDriveTemp",
     
        // Dev noise — safe to exclude for all users
        "node_modules",
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
    ];

    public List<string> ExcludedExtensions { get; init; } =     [
        ".sys",   // kernel / driver files
        ".etl",   // Windows event trace logs (can be gigabytes)
        ".dmp",   // crash dump files
    ];

    public int? MaxDepth { get; init; } = null;
    public long? MaxFileSizeBytes { get; init; } = null;
}