using System.IO;
using SDMS.Domain.Models;

namespace SDMS.Infrastructure.Scanner;

/// <summary>
/// Contract for OS-level metadata reads. 
/// Abstracted so the scanner can be unit-tested without performing actual disk I/O.
/// </summary>
public interface IMetaDataExtractor_Interface
{
    /// <summary>
    /// Reads OS attributes for a file or directory. 
    /// Should catch access exceptions and return a record with PermissionError = true 
    /// to ensure the scan continues.
    /// </summary>
    ExtractedMetaData Extract(FileSystemInfo entry);

    /// <summary>
    /// Resolves the destination of a symbolic link. 
    /// Returns null if the entry is not a link or resolution is unsupported.
    /// </summary>
    string? ResolveSymlinkTarget(FileSystemInfo entry);
}