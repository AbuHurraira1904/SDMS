using System.Windows;
using Application = System.Windows.Application;   // disambiguate from SDMS.Application namespace
using SDMS.UI;

namespace SDMS;

public partial class App : System.Windows.Application
{
    private LocalApiServer? _server;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _server = new LocalApiServer();
        await _server.StartAsync();
        var mainWindow = new MainWindow(_server);
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_server is not null)
            await _server.StopAsync();
        base.OnExit(e);
    }
}