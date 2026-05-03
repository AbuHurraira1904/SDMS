namespace SDMS.Domain.Scanner;

/// <summary>
/// Configuration passed to the Directory Scanner.
/// Controls what gets included or excluded during traversal.
/// </summary>
public class ScanOptions
{
    /// <summary>Follow symbolic links during traversal. Default false to avoid cycles.</summary>
    public bool FollowSymlinks { get; init; } = false;

    /// <summary>Include hidden files and directories (dot-files on Unix, Hidden attribute on Windows).</summary>
    public bool IncludeHidden { get; init; } = false;

    /// <summary>Include files marked as System.</summary>
    public bool IncludeSystemFiles { get; init; } = false;

    /// <summary>
    /// Directory names to skip entirely during traversal.
    /// e.g. ["node_modules", ".git", "$RECYCLE.BIN"]
    /// </summary>
    public List<string> ExcludedDirectoryNames { get; init; } = new()
    {
        "node_modules",
        ".git",
        ".vs",
        "$RECYCLE.BIN",
        "System Volume Information"
    };

    /// <summary>File extensions to exclude. Lowercase with dot, e.g. [".tmp", ".log"].</summary>
    public List<string> ExcludedExtensions { get; init; } = new();

    /// <summary>Maximum directory depth to traverse. Null means unlimited.</summary>
    public int? MaxDepth { get; init; } = null;

    /// <summary>Skip files larger than this size in bytes. Null means no limit.</summary>
    public long? MaxFileSizeBytes { get; init; } = null;
}
