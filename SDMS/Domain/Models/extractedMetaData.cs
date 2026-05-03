using System.IO;

namespace SDMS.Domain.Models;

/// <summary>
/// Intermediate DTO mapping raw OS data to domain models.
/// Short-lived — discarded after TreeWalker builds the corresponding FileNode.
/// </summary>
public sealed record ExtractedMetaData
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public required long SizeBytes { get; init; }

    // Null when the OS or filesystem (e.g. some Linux ext4 configs) doesn't record it
    public DateTime? CreatedAt { get; init; }
    
    public required DateTime ModifiedAt { get; init; }
    public required DateTime AccessedAt { get; init; }

    // Combined OS flags (Hidden, System, ReparsePoint) to keep the record flat
    public required FileAttributes Attributes { get; init; }

    public string? SymlinkTarget { get; init; } = null;

    // Indicates if the OS call (stat/GetFileAttributes) failed due to access restrictions
    public required bool PermissionError { get; init; }
}