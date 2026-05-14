using System.IO;
using System.Windows;
using System.Windows.Forms;           // FolderBrowserDialog — add reference to System.Windows.Forms
using Microsoft.Web.WebView2.Core;
using SDMS.UI;
using MessageBox = System.Windows.Forms.MessageBox;

namespace SDMS;

/// <summary>
/// WPF shell — contains only a full-screen WebView2 that renders the React app.
///
/// Two-way bridge with the React frontend:
///   React → C#:  window.chrome.webview.postMessage({ type, payload })
///   C# → React:  webView.CoreWebView2.PostWebMessageAsJson(...)
///
/// Currently handled message types:
///   "PICK_FOLDER"  — opens the OS folder picker, returns chosen path to React
/// </summary>
public partial class MainWindow : Window
{
    private readonly LocalApiServer _server;

    // Absolute path to the Vite production build.
    // Place your `npm run build` output at:  <solution-root>/frontend/dist/
    private static readonly string DistFolder =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "UI", "Web", "dist"));

    public MainWindow(LocalApiServer server)
    {
        InitializeComponent();
        _server = server;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await WebView.EnsureCoreWebView2Async();
        
        WebView.CoreWebView2.OpenDevToolsWindow();
        
        var core = WebView.CoreWebView2;

        // ── Security: only allow the local origin ──────────────────────────
        // Serve the React dist folder as a virtual host so the browser sees
        // https://sdms.local/ instead of a file:// URL.
        // file:// URLs can't make fetch() calls to localhost in WebView2.
        MessageBox.Show(DistFolder);

        if (!Directory.Exists(DistFolder))
        {
            MessageBox.Show("Dist folder not found!");
            return;
        }
        core.SetVirtualHostNameToFolderMapping(
            "sdms.local",
            DistFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        // Suppress default context menu and DevTools in production
#if !DEBUG
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled            = false;
#endif

        // ── Bridge: React → C# messages ───────────────────────────────────
        core.WebMessageReceived += OnWebMessageReceived;

        // ── Navigate to the React app ──────────────────────────────────────
        core.Navigate("http://sdms.local/index.html");
    }

    /// <summary>
    /// Handles messages posted from React via window.chrome.webview.postMessage().
    /// Expected message format:  { "type": "PICK_FOLDER", "requestId": "abc123" }
    /// </summary>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            var msg = System.Text.Json.JsonSerializer.Deserialize<BridgeMessage>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (msg is null) return;

            switch (msg.Type)
            {
                case "PICK_FOLDER":
                    HandlePickFolder(msg.RequestId);
                    break;
            }
        }
        catch { /* Malformed message — ignore */ }
    }

    /// <summary>
    /// Opens the Windows folder picker on the UI thread, then posts the result
    /// back to React as:  { "type": "PICK_FOLDER_RESULT", "requestId": "...", "path": "C:\\..." }
    /// </summary>
    private void HandlePickFolder(string? requestId)
    {
        // Must run on the UI (STA) thread.
        Dispatcher.Invoke(() =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description         = "Select the directory to organize",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
            };

            string? chosenPath = null;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                chosenPath = dlg.SelectedPath;

            // Post result back to React
            var response = System.Text.Json.JsonSerializer.Serialize(new
            {
                type      = "PICK_FOLDER_RESULT",
                requestId = requestId,
                path      = chosenPath   // null if user cancelled
            });

            WebView.CoreWebView2.PostWebMessageAsJson(response);
        });
    }

    private sealed record BridgeMessage(string Type, string? RequestId);
}