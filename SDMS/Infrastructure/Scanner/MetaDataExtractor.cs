using System.IO;
using SDMS.Domain.Models;

namespace SDMS.Infrastructure.Scanner;

/// <summary>
/// Concrete implementation of metadata extraction using standard System.IO APIs.
/// Catches I/O and Permission exceptions to return flagged partial records.
/// </summary>
public sealed class MetaDataExtractor : IMetaDataExtractor_Interface
{
    public ExtractedMetaData Extract(FileSystemInfo entry)
    {
        try
        {
            return entry switch
            {
                FileInfo fi      => BuildFromFile(fi),
                DirectoryInfo di => BuildFromDirectory(di),
                _                => BuildGeneric(entry),
            };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return ErrorRecord(entry);
        }
    }

    public string? ResolveSymlinkTarget(FileSystemInfo entry)
    {
        try
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                return null;

            // .NET 6+ API: false returns immediate target; true follows the full chain
            return entry.ResolveLinkTarget(returnFinalTarget: false)?.FullName;
        }
        catch { return null; } // Occurs with broken symlinks or locked reparse points
    }

    private ExtractedMetaData BuildFromFile(FileInfo fi) => new()
    {
        Name = fi.Name,
        FullPath = fi.FullName,
        IsDirectory = false,
        SizeBytes = fi.Length,
        CreatedAt = SanitiseDate(fi.CreationTimeUtc),
        ModifiedAt = fi.LastWriteTimeUtc,
        AccessedAt = fi.LastAccessTimeUtc,
        Attributes = fi.Attributes,
        SymlinkTarget = ResolveSymlinkTarget(fi),
        PermissionError = false
    };

    private ExtractedMetaData BuildFromDirectory(DirectoryInfo di) => new()
    {
        Name = di.Name,
        FullPath = di.FullName,
        IsDirectory = true,
        SizeBytes = 0,
        CreatedAt = SanitiseDate(di.CreationTimeUtc),
        ModifiedAt = di.LastWriteTimeUtc,
        AccessedAt = di.LastAccessTimeUtc,
        Attributes = di.Attributes,
        SymlinkTarget = ResolveSymlinkTarget(di),
        PermissionError = false
    };

    private ExtractedMetaData BuildGeneric(FileSystemInfo entry) => new()
    {
        Name = entry.Name,
        FullPath = entry.FullName,
        IsDirectory = (entry.Attributes & FileAttributes.Directory) != 0,
        SizeBytes = 0,
        CreatedAt = SanitiseDate(entry.CreationTimeUtc),
        ModifiedAt = entry.LastWriteTimeUtc,
        AccessedAt = entry.LastAccessTimeUtc,
        Attributes = entry.Attributes,
        SymlinkTarget = ResolveSymlinkTarget(entry),
        PermissionError = false
    };

    private static ExtractedMetaData ErrorRecord(FileSystemInfo entry) => new()
    {
        Name = entry.Name,
        FullPath = entry.FullName,
        IsDirectory = false,
        SizeBytes = 0,
        CreatedAt = null,
        ModifiedAt = DateTime.MinValue,
        AccessedAt = DateTime.MinValue,
        Attributes = FileAttributes.Normal,
        PermissionError = true
    };

    /// <summary>
    /// Linux/ext4 often returns Unix Epoch or MinValue for birthtime/creation dates.
    /// We treat these as null to avoid misleading "1970" timestamps.
    /// </summary>
    private static DateTime? SanitiseDate(DateTime dt) =>
        dt == DateTime.UnixEpoch || dt == DateTime.MinValue ? null : dt;
}