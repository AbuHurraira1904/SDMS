using System.IO;

namespace SDMS.SDMS.Domain.Models;

public sealed record FileNode
{
    public string Name { get; init; }
    public string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public DateTime AccessedAt { get; init; }
    public FileAttributes Attributes { get; init; }   // Hidden, System, ReadOnly, etc.
    public List<FileNode> Children { get; init; }     // Empty if file
    public string? SymlinkTarget { get; init; }       // Null if not a symlink.
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name).ToLowerInvariant();
    public string? Hash { get; set; } = null;
    public required string MimeType { get; init; }
}