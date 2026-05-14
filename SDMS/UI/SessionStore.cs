using System.Collections.Concurrent;
using SDMS.Domain.Brain;
using SDMS.Domain.Models;
using SDMS.Domain.Scanner;
using SDMS.Infrastructure.PlanEditor;

namespace SDMS.UI;

/// <summary>
/// Thread-safe in-memory store.  One entry per user scan session.
/// Lives for the lifetime of the WPF process.
/// </summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, Session> _store = new();

    public Session Create()
    {
        var s = new Session { Id = Guid.NewGuid().ToString() };
        _store[s.Id] = s;
        return s;
    }

    public Session? Get(string id) =>
        _store.TryGetValue(id, out var s) ? s : null;

    public bool TryGet(string id, out Session session)
    {
        session = null!;
        if (_store.TryGetValue(id, out var s)) { session = s; return true; }
        return false;
    }
}

/// <summary>
/// All data produced in a single scan → analyze → plan → execute lifecycle.
/// </summary>
public sealed class Session
{
    public required string Id { get; init; }

    // Step 1 — scan
    public FileTree?     Tree          { get; set; }
    public string?       TreeJsonPath  { get; set; }   // path on disk, sent to brain
    public string?       ScanRoot      { get; set; }
    public ScanOptions?  ScanOptions   { get; set; }

    // Step 2 — brain analysis
    public PlanOutput?   PlanOutput    { get; set; }

    // Step 3 — editor
    public PlanEditor?   Editor        { get; set; }

    // Step 3b — finalized plan (after editor finalize)
    public SDMS.Domain.Models.FinalizedPlan?   FinalizedPlan { get; set; }

    // Step 4 — execution log
    public SDMS.Domain.Execution.ExecutionLog? ExecutionLog { get; set; }
}
