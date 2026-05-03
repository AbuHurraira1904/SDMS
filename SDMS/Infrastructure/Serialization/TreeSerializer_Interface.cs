// ============================================================
// TreeSerializer_Interface.cs  →  SDMS.Infrastructure/Serialization/
// Contract for writing a FileTree to disk.
// Implementations: JSONTreeSerializer, MsgTreeSerializer.
// Swap format by changing one DI registration — no code changes.
// ============================================================
using SDMS.Domain.Models;

namespace SDMS.Infrastructure.Serialization;

/// <summary>
/// Serialises a <see cref="FileTree"/> to a file on disk and reads it back.
/// Abstracted so the output format (JSON, MessagePack, Parquet…) is a
/// deployment-time decision.
/// </summary>
public interface ITreeSerializer_Interface
{
    /// <summary>Writes <paramref name="tree"/> to <paramref name="outputPath"/>.</summary>
    /// <param name="tree">Fully-populated scan result to persist.</param>
    /// <param name="outputPath">Destination file path (created or overwritten).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Absolute path of the file that was written.</returns>
    Task<string> SerializeAsync(
        FileTree          tree,
        string            outputPath,
        CancellationToken ct = default);

    /// <summary>
    /// Reads a previously serialised <see cref="FileTree"/> from <paramref name="inputPath"/>.
    /// </summary>
    Task<FileTree> DeserializeAsync(
        string            inputPath,
        CancellationToken ct = default);
}
