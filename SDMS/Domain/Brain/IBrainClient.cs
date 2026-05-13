// ============================================================
// IBrainClient.cs  →  SDMS.Domain/Brain/
// Interface so the UI never depends on HttpClient directly.
// ============================================================
namespace SDMS.Domain.Brain;

public interface IBrainClient
{
    Task<bool>       IsAliveAsync(CancellationToken ct = default);
    Task<PlanOutput> AnalyzeAsync(
        string fileTreeJsonPath,
        bool   readContent = false,
        bool   allowHidden = false,
        bool   allowSystem = false,
        CancellationToken ct = default);
}