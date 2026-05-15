using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace SDMS;

public partial class MainWindow : Window
{
    // The local ASP.NET Core server listens here.
    // Must match LocalApiServer.ApiBase and the React .env.local.
    private const string ApiOrigin = "http://localhost:5001";

    public MainWindow()
    {
        InitializeComponent();
        InitializeWebView();
    }

    // ── WebView2 initialisation ───────────────────────────────────────────────

    private async void InitializeWebView()
    {
        // UserDataFolder keeps the browser cache/profile separate from AppData
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SDMS", "WebView2");

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: new CoreWebView2EnvironmentOptions
            {
                // Allow the React app (served from file://) to call our HTTP
                // API on localhost without CORS blocking.
                AdditionalBrowserArguments =
                    "--disable-web-security " +
                    "--allow-running-insecure-content"
            });

        await WebView.EnsureCoreWebView2Async(env);
    }

    // Fires after EnsureCoreWebView2Async completes
    private void WebView_Initialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            MessageBox.Show(
                $"WebView2 failed to initialize:\n{e.InitializationException?.Message}",
                "SDMS — Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            System.Windows.Application.Current.Shutdown(1);
            return;
        }

        var core = WebView.CoreWebView2;

        // ── Security: only allow our local API origin ─────────────────────────
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled            = false;
        core.Settings.AreDevToolsEnabled            = true;   // set false for release

        // ── Register the folder picker COM host object ────────────────────────
        // folderPicker.ts calls:
        //   window.chrome.webview.hostObjects.sync.folderPicker.PickFolder()
        var picker = new FolderPickerHostObject(this);
        core.AddHostObjectToScript("folderPicker", picker);

        // ── Navigate to the React app ─────────────────────────────────────────
        // Production: load the built dist/index.html embedded as wwwroot content.
        // Dev override: if the Vite dev server is running, navigate there instead
        // so you get HMR. Controlled by SDMS_DEV_MODE env var.
        var devMode = Environment.GetEnvironmentVariable("SDMS_DEV_MODE");
        if (!string.IsNullOrEmpty(devMode))
        {
            // Vite dev server — hot reload works
            core.Navigate("http://localhost:5173");
        }
        else
        {
            // Production build embedded as wwwroot/
            var distIndex = Path.Combine(
                AppContext.BaseDirectory, "wwwroot", "index.html");

            if (File.Exists(distIndex))
                core.Navigate(new Uri(distIndex).AbsoluteUri);
            else
                core.NavigateToString(FallbackHtml());
        }

        // ── Allow the React app to communicate back via postMessage if needed ─
        core.WebMessageReceived += OnWebMessageReceived;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Reserved for future use — React can post messages via
        // window.chrome.webview.postMessage("...") if needed.
        var msg = e.TryGetWebMessageAsString();
        System.Diagnostics.Debug.WriteLine($"[WebView] Message: {msg}");
    }

    // ── Fallback page shown if the dist build is missing ─────────────────────

    private static string FallbackHtml() => """
        <!DOCTYPE html>
        <html>
        <body style="background:#0f0f0f;color:#fff;font-family:sans-serif;
                     display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
          <div style="text-align:center">
            <h2>React build not found</h2>
            <p>Run <code>pnpm run build</code> inside <code>UI/Web/</code> first,<br>
               or set <code>SDMS_DEV_MODE=1</code> and start the Vite dev server.</p>
          </div>
        </body>
        </html>
        """;
}

// ── COM-visible host object ───────────────────────────────────────────────────

/// <summary>
/// Exposed to JavaScript as window.chrome.webview.hostObjects.sync.folderPicker
/// folderPicker.ts calls PickFolder() to open the native Windows folder dialog.
/// Must be [ComVisible] and have a default COM-compatible interface.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class FolderPickerHostObject
{
    private readonly Window _owner;

    public FolderPickerHostObject(Window owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Opens a native WPF folder browser dialog.
    /// Returns the selected path, or an empty string if the user cancels.
    /// Called synchronously from JS via hostObjects.sync.folderPicker.PickFolder()
    /// </summary>
    public string PickFolder()
    {
        string result = string.Empty;

        // Must run on the UI thread
        _owner.Dispatcher.Invoke(() =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title            = "Select a folder to organize",
                Multiselect      = false,
            };

            if (dialog.ShowDialog(_owner) == true)
                result = dialog.FolderName;
        });

        return result;
    }
}