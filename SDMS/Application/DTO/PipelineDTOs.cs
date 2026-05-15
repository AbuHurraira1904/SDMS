// ============================================================
// PipelineDTOs.cs  →  SDMS.Application/DTOs/
// Data shapes that cross the Application → UI boundary.
// The UI only imports this file — never raw Domain models directly.
// ============================================================

using SDMS.Domain.Execution;
using SDMS.Domain.Models;
using SDMS.Domain.PlanEditor;

// Resolve the ValidationResult ambiguity: Execution.ValidationResult is a
// subclass of PlanEditor.ValidationResult, so we use the base type everywhere.
using ValidationResult = SDMS.Domain.PlanEditor.ValidationResult;

namespace SDMS.Application.DTOs;

// ── Inbound (UI → Application) ────────────────────────────────────────────────

/// <summary>
/// Everything the UI collects before starting a pipeline run.
/// </summary>
public sealed class ScanRequest
{
    public required string RootPath    { get; init; }
    public bool IncludeHidden          { get; init; } = false;
    public bool IncludeSystemFiles     { get; init; } = false;
    public bool FollowSymlinks         { get; init; } = false;
    public int? MaxDepth               { get; init; } = null;
    public long? MaxFileSizeBytes      { get; init; } = null;
    public List<string> ExcludedDirs   { get; init; } = new();
    public List<string> ExcludedExts   { get; init; } = new();
}

/// <summary>
/// Options the UI passes when the user clicks Execute.
/// </summary>
public sealed class ExecuteRequest
{
    public bool UseStagingForDeletes { get; init; } = true;
    public bool DryRun               { get; init; } = false;
    public bool RollbackOnFailure    { get; init; } = true;
    public string? CustomStagingPath { get; init; } = null;
}

// ── Outbound (Application → UI) ───────────────────────────────────────────────

/// <summary>
/// Emitted after the Scan phase completes. Gives the UI enough to
/// render a basic summary without exposing the full FileTree graph.
/// </summary>
public sealed class ScanResult
{
    public required FileTree  Tree            { get; init; }
    public required int       TotalFiles      { get; init; }
    public required int       TotalDirs       { get; init; }
    public required long      TotalBytes      { get; init; }
    public required TimeSpan  Elapsed         { get; init; }
    public required DateTime  ScannedAt       { get; init; }
}

/// <summary>
/// Emitted after the Analysis phase completes.
/// </summary>
public sealed class AnalysisResult
{
    public required AnalysisReport        Report          { get; init; }
    public required Dictionary<string,int> TopExtensions  { get; init; }   // top 10 by count
    public required Dictionary<string,int> CategoryCounts { get; init; }
    public required List<string>           SuggestedLabels{ get; init; }
    public required TimeSpan               Elapsed         { get; init; }
}

/// <summary>
/// Emitted after the Scoring phase completes.
/// </summary>
public sealed class ScoringResult
{
    public required List<ScoredFileNode> ScoredFiles { get; init; }
    public required int    HighScoreCount            { get; init; }   // score ≥ 70
    public required int    LowScoreCount             { get; init; }   // score < 30
    public required double AverageScore              { get; init; }
    public required TimeSpan Elapsed                 { get; init; }
}

/// <summary>
/// Emitted after the Brain generates a ProposedPlan.
/// The UI renders this in the plan review screen.
/// </summary>
public sealed class PlanResult
{
    public required ProposedPlan           Plan          { get; init; }
    public required int                    OperationCount{ get; init; }
    public required TimeSpan               Elapsed       { get; init; }
}

/// <summary>
/// Emitted after the user finalizes their edits and the ExecutionEngine runs.
/// </summary>
public sealed class ExecutionResult
{
    public required ExecutionLog   Log           { get; init; }
    public required ExecutionState FinalState    { get; init; }
    public required int            SuccessCount  { get; init; }
    public required int            FailureCount  { get; init; }
    public required bool           IsFullSuccess { get; init; }
    public required TimeSpan       Elapsed       { get; init; }
}

/// <summary>
/// Wraps preflight output in a UI-friendly shape.
/// </summary>
public sealed class PreflightResult
{
    public required ValidationResult Validation { get; init; }
    public required bool             CanExecute { get; init; }
}

/// <summary>
/// Unified pipeline progress event emitted to the UI via IProgress.
/// One type covers all phases so the UI only registers one handler.
/// </summary>
public sealed class PipelineProgress
{
    public required PipelinePhase Phase      { get; init; }
    public required string        Message    { get; init; }
    public          int?          FilesCount { get; init; }   // scan phase
    public          int?          Completed  { get; init; }   // execution phase
    public          int?          Total      { get; init; }   // execution phase
}

public enum PipelinePhase
{
    Scanning,
    Analyzing,
    Scoring,
    GeneratingPlan,
    AwaitingUserReview,
    Preflight,
    Executing,
    Complete,
    Failed,
}