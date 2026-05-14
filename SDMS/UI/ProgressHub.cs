using Microsoft.AspNetCore.SignalR;

namespace SDMS.UI;

/// <summary>
/// SignalR hub that streams progress events to the React frontend.
///
/// React connects with:
///   const conn = new signalR.HubConnectionBuilder()
///     .withUrl("http://localhost:5001/api/hubs/progress")
///     .build();
///   await conn.start();
///   conn.invoke("JoinSession", sessionId);
///   conn.on("Progress", (event) => { ... });
///
/// Event shapes sent from the server:
///   { type: "scan",      filesScanned, currentPath }
///   { type: "execute",   completed, total, operationType, sourcePath }
///   { type: "done",      message }
///   { type: "error",     message }
/// </summary>
public sealed class ProgressHub : Hub
{
    /// <summary>
    /// React calls this after connecting so it only receives events for its session.
    /// </summary>
    public Task JoinSession(string sessionId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

    public Task LeaveSession(string sessionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
}

/// <summary>
/// Typed helper — injected into endpoint handlers to push events.
/// </summary>
public static class ProgressHubExtensions
{
    public static Task SendScanProgress(
        IHubContext<ProgressHub> hub, string sessionId,
        int filesScanned, string currentPath) =>
        hub.Clients.Group(sessionId).SendAsync("Progress", new
        {
            type         = "scan",
            filesScanned,
            currentPath,
        });

    public static Task SendExecuteProgress(
        IHubContext<ProgressHub> hub, string sessionId,
        int completed, int total, string operationType, string sourcePath) =>
        hub.Clients.Group(sessionId).SendAsync("Progress", new
        {
            type = "execute",
            completed,
            total,
            operationType,
            sourcePath,
        });

    public static Task SendDone(IHubContext<ProgressHub> hub, string sessionId, string message) =>
        hub.Clients.Group(sessionId).SendAsync("Progress", new { type = "done", message });

    public static Task SendError(IHubContext<ProgressHub> hub, string sessionId, string message) =>
        hub.Clients.Group(sessionId).SendAsync("Progress", new { type = "error", message });
}
