using System.Windows;
using SDMS.UI;

namespace SDMS;

public partial class App : System.Windows.Application
{
    private LocalApiServer? _apiServer;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Boot the local ASP.NET Core bridge first — WebView2 will start
        // making fetch() calls as soon as the React app loads, so the server
        // must be ready before the window appears.
        _apiServer = new LocalApiServer();
        await _apiServer.StartAsync();

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_apiServer is not null)
            await _apiServer.StopAsync();

        base.OnExit(e);
    }
}
