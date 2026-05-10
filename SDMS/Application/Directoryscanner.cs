// ============================================================
// Directoryscanner.cs  →  SDMS.Application/Scanner/
// Concrete implementation of IDirectoryScanner.
// Orchestrates ITreeWalk_Interface → FileTree → ITreeSerializer_Interface.
// Has ZERO direct OS calls — all I/O is via injected interfaces.
// ============================================================

using System.IO;
using SDMS.Domain.Models;
using SDMS.Domain.Scanner;
using SDMS.Infrastructure.Scanner;
using SDMS.Infrastructure.Serialization;

namespace SDMS.Application;

/// <summary>
/// Application-layer facade implementing <see cref="IDirectoryScanner"/>.
/// Wraps <see cref="ITreeWalk_Interface"/> output into a complete
/// <see cref="FileTree"/> and optionally persists it via
/// <see cref="ITreeSerializer_Interface"/>.
///
/// Constructor injection — never news up infrastructure types directly.
/// </summary>
public sealed class Directoryscanner : IDirectoryScanner
{
    private readonly ITreeWalk_Interface      _walker;
    private readonly ITreeSerializer_Interface _serializer;

    /// <param name="walker">
    /// Injected walker — provides async filesystem traversal.
    /// </param>
    /// <param name="serializer">
    /// Injected serialiser — used when <see cref="SaveAsync"/> is called.
    /// Defaults to <see cref="JSONTreeSerializer"/> when not supplied.
    /// </param>
    public Directoryscanner(
        ITreeWalk_Interface       walker,
        ITreeSerializer_Interface? serializer = null)
    {
        _walker     = walker;
        _serializer = serializer ?? new JSONTreeSerializer();
    }

    /// <inheritdoc/>
    public async Task<FileTree> ScanAsync(
        string                                              rootPath,
        ScanOptions                                         options,
        IProgress<(int FilesScanned, string CurrentPath)>? progress = null,
        CancellationToken                                   ct       = default)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(
                $"Root path does not exist or is not a directory: '{rootPath}'");

        var startedAt = DateTime.UtcNow;

        // Delegate all traversal to the injected walker.
        var root = await _walker.WalkAsync(rootPath, options, progress, ct);

        return new FileTree
        {
            Root             = root,
            ScannedAt        = startedAt,
            ScanRootPath     = Path.GetFullPath(rootPath),
            TotalFiles       = _walker.TotalFiles,
            TotalDirectories = _walker.TotalDirectories,
            TotalSizeBytes   = _walker.TotalSizeBytes,
            TotalIgnoredFiles = _walker.TotalIgnoredFiles,
            SkippedPaths     = [.. _walker.SkippedPaths],
        };
    }

    /// <summary>
    /// Persists a previously scanned <see cref="FileTree"/> to disk.
    /// </summary>
    /// <param name="tree">Result of <see cref="ScanAsync"/>.</param>
    /// <param name="outputPath">Destination file path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Absolute path of the written file.</returns>
    public Task<string> SaveAsync(
        FileTree          tree,
        string            outputPath,
        CancellationToken ct = default) =>
        _serializer.SerializeAsync(tree, outputPath, ct);

    /// <summary>
    /// Loads a previously persisted <see cref="FileTree"/> from disk.
    /// Useful for resuming downstream processing without re-scanning.
    /// </summary>
    public Task<FileTree> LoadAsync(
        string            inputPath,
        CancellationToken ct = default) =>
        _serializer.DeserializeAsync(inputPath, ct);
}
