// ============================================================
// ScanUseCase.cs  →  SDMS.Application/UseCases/
// Phase 1 of the SDMS pipeline.
// Wraps IDirectoryScanner and translates its output into a
// ScanResult DTO the UI can bind to directly.
// ============================================================

using SDMS.Application.DTOs;
using SDMS.Domain.Models;
using SDMS.Domain.Scanner;

namespace SDMS.Application.UseCases;

public sealed class ScanUseCase
{
    private readonly IDirectoryScanner _scanner;

    public ScanUseCase(IDirectoryScanner scanner)
    {
        _scanner = scanner;
    }

    /// <summary>
    /// Scans <paramref name="request.RootPath"/> and returns a populated ScanResult.
    /// Throws <see cref="DirectoryNotFoundException"/> if the root doesn't exist.
    /// </summary>
    public async Task<ScanResult> ExecuteAsync(
        ScanRequest                                              request,
        IProgress<PipelineProgress>?                             progress = null,
        CancellationToken                                        ct       = default)
    {
        var options = BuildOptions(request);

        // Wrap the raw scanner progress into our unified PipelineProgress type
        var scanProgress = progress is null ? null :
            new Progress<(int FilesScanned, string CurrentPath)>(r =>
                progress.Report(new PipelineProgress
                {
                    Phase      = PipelinePhase.Scanning,
                    Message    = r.CurrentPath,
                    FilesCount = r.FilesScanned,
                }));

        var started = DateTime.UtcNow;

        progress?.Report(new PipelineProgress
        {
            Phase   = PipelinePhase.Scanning,
            Message = $"Starting scan of '{request.RootPath}' …",
        });

        var tree    = await _scanner.ScanAsync(request.RootPath, options, scanProgress, ct);
        var elapsed = DateTime.UtcNow - started;
        var info    = tree.BasicInfo;

        progress?.Report(new PipelineProgress
        {
            Phase      = PipelinePhase.Scanning,
            Message    = $"Scan complete — {info.TotalFiles:N0} files in {elapsed.TotalSeconds:F2}s",
            FilesCount = info.TotalFiles,
        });

        return new ScanResult
        {
            Tree       = tree,
            TotalFiles = info.TotalFiles,
            TotalDirs  = info.TotalDirectories,
            TotalBytes = info.TotalSizeBytes,
            Elapsed    = elapsed,
            ScannedAt  = tree.ScannedAt,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ScanOptions BuildOptions(ScanRequest req)
    {
        var opts = new ScanOptions
        {
            FollowSymlinks     = req.FollowSymlinks,
            IncludeHidden      = req.IncludeHidden,
            IncludeSystemFiles = req.IncludeSystemFiles,
            MaxDepth           = req.MaxDepth,
            MaxFileSizeBytes   = req.MaxFileSizeBytes,
        };

        // Merge caller exclusions on top of defaults
        if (req.ExcludedDirs.Count > 0)
        {
            var merged = new List<string>(opts.ExcludedDirectoryNames);
            merged.AddRange(req.ExcludedDirs);
            return opts with { ExcludedDirectoryNames = merged };
        }

        if (req.ExcludedExts.Count > 0)
        {
            var merged = new List<string>(opts.ExcludedExtensions);
            merged.AddRange(req.ExcludedExts);
            return opts with { ExcludedExtensions = merged };
        }

        return opts;
    }
}
