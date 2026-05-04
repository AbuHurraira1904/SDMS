using System.IO;

namespace SDMS.Domain.Models;

public sealed record FileNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime ModifiedAt { get; init; }
    public required DateTime AccessedAt { get; init; }
    public required FileAttributes Attributes { get; init; }
    public required string MimeType { get; init; }
    
    public List<FileNode> Children { get; init; } = [];
    public string? SymlinkTarget { get; init; } = null;
    public string? Hash { get; set; } = null;

    public string Extension => IsDirectory 
        ? string.Empty 
        : Path.GetExtension(Name).TrimStart('.').ToLowerInvariant();

    public bool IsHidden   => (Attributes & FileAttributes.Hidden) != 0;
    public bool IsSystem   => (Attributes & FileAttributes.System) != 0;
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;
    public bool IsSymlink  => (Attributes & FileAttributes.ReparsePoint) != 0;
    
}