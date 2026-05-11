using System.IO;
using SDMS.Domain.Models;
using SDMS.Domain.Execution;

namespace SDMS.Infrastructure.Execution;

public sealed class ExecutionEngine : IExecutionEngine
{
    public ExecutionState State { get; private set; } = ExecutionState.Idle;

    public async Task<ValidationResult> PreflightAsync(FinalizedPlan plan, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var op in plan.Operations)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(op.SourcePath) && !File.Exists(op.SourcePath) && !Directory.Exists(op.SourcePath))
            {
                errors.Add($"Source not found: {op.SourcePath}");
            }

            if (!string.IsNullOrEmpty(op.SourcePath) && !string.IsNullOrEmpty(op.DestinationPath))
            {
                // Verify intra-partition constraint
                if (Path.GetPathRoot(op.SourcePath) != Path.GetPathRoot(op.DestinationPath))
                {
                    errors.Add($"Cross-volume move blocked: {op.SourcePath} -> {op.DestinationPath}");
                }
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors, Warnings = warnings };
    }

    public async Task<ExecutionLog> ExecuteAsync(
        FinalizedPlan plan,
        ExecutionOptions? options = null,
        IProgress<(int Completed, int Total, PlannedOperation Current)>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new ExecutionOptions();
        var log = new ExecutionLog { PlanId = plan.Id, StartedAt = DateTime.UtcNow };
        var results = new List<OperationResult>();
        
        State = ExecutionState.Executing;

        for (int i = 0; i < plan.Operations.Count; i++)
        {
            var op = plan.Operations[i];
            var result = new OperationResult { OperationId = op.Id, OperationType = op.Type, ExecutedAt = DateTime.UtcNow };

            try 
            {
                if (ct.IsCancellationRequested) break;

                progress?.Report((i, plan.Operations.Count, op));

                if (options.DryRun)
                {
                    results.Add(new OperationResult { OperationId = op.Id, Success = true });
                    continue;
                }

                // Process and capture the staging path via the return tuple
                var (success, stagingPath) = await ProcessOperationAsync(op, options);
                
                results.Add(new OperationResult 
                { 
                    OperationId = op.Id, 
                    Success = success, 
                    StagingPath = stagingPath 
                });
            }
            catch (Exception ex)
            {
                results.Add(new OperationResult { OperationId = op.Id, Success = false, ErrorMessage = ex.Message });
            }
        }

        log.Results.AddRange(results);
        log.EndedAt = DateTime.UtcNow;
        State = log.FailureCount > 0 ? ExecutionState.PartialFailure : ExecutionState.Completed;
        log.FinalState = State;

        return log;
    }

    private async Task<(bool Success, string? StagingPath)> ProcessOperationAsync(PlannedOperation op, ExecutionOptions options)
    {
        string? stagingPath = null;
    
        // Safety check: Ensure the parent directory exists for any move/rename
        if (!string.IsNullOrEmpty(op.DestinationPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(op.DestinationPath)!);
        }

        switch (op.Type)
        {
            case OpType.Move:
            case OpType.MoveFolder:
            case OpType.RenameFolder:
                string finalDest = GetUniquePath(op.DestinationPath!);
                if (op.IsDirectoryOp) Directory.Move(op.SourcePath!, finalDest);
                else File.Move(op.SourcePath!, finalDest);
                break;

            case OpType.Delete:
            case OpType.DeleteFolder:
                if (options.UseStagingForDeletes)
                {
                    stagingPath = GetHiddenStagingPath(op.SourcePath!);
                    if (op.IsDirectoryOp) Directory.Move(op.SourcePath!, stagingPath);
                    else File.Move(op.SourcePath!, stagingPath);
                }
                else
                {
                    if (op.IsDirectoryOp) Directory.Delete(op.SourcePath!, recursive: true);
                    else File.Delete(op.SourcePath!);
                }
                break;

            case OpType.NewFolder:
                Directory.CreateDirectory(op.DestinationPath!);
                break;
            
            case OpType.MergeFolder:
                // This would call a separate recursive helper method
                //await MergeDirectoriesAsync(op.SourcePath!, op.DestinationPath!);
                break;
        }

        return (true, stagingPath);
    }

    /// <summary>
    /// Implements the (1), (2), (3) renaming logic for collisions.
    /// </summary>
    private string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;

        string directory = Path.GetDirectoryName(path)!;
        string filename = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        int count = 1;

        string newPath;
        do
        {
            newPath = Path.Combine(directory, $"{filename} ({count++}){extension}");
        } while (File.Exists(newPath) || Directory.Exists(newPath));

        return newPath;
    }

    private string GetHiddenStagingPath(string sourcePath)
    {
        var root = Path.GetPathRoot(sourcePath)!;
        var stagingDir = Path.Combine(root, ".sdms_staging");
        
        if (!Directory.Exists(stagingDir))
        {
            var di = Directory.CreateDirectory(stagingDir);
            di.Attributes |= FileAttributes.Hidden;
        }

        return Path.Combine(stagingDir, $"{Guid.NewGuid()}_{Path.GetFileName(sourcePath)}");
    }

    public async Task<ExecutionLog> RollbackAsync(ExecutionLog log, CancellationToken ct = default)
    {
        State = ExecutionState.RollingBack;
        // Reverse iteration logic would go here
        State = ExecutionState.RolledBack;
        return log;
    }
}