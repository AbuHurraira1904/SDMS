namespace SDMS.Infrastructure.Scanner;

/// <summary>
/// Detects filesystem mount points and network drives.
/// Abstracted to allow testing without relying on physical drive state or OS-level mount tables.
/// </summary>
public interface IMountDectector_Interface
{
    /// <summary>
    /// Returns true if the path is a mount point.
    /// (e.g., a different device ID on POSIX, or a volume/UNC root on Windows).
    /// </summary>
    bool IsMount(string fullPath);

    /// <summary>
    /// Returns true if the path is the root of a remote/network resource (NFS, SMB, or mapped drive).
    /// </summary>
    bool IsNetworkDrive(string fullPath);
}