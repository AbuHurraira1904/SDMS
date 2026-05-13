// BrainProcessManager.cs  →  SDMS.Infrastructure/Brain/
// Starts the Python Flask server as a child process when the app launches.
// Stops it cleanly when the app exits.
//
// Usage in Program.cs / DI setup:
//   services.AddSingleton<BrainProcessManager>();
//   then call  app.Services.GetRequiredService<BrainProcessManager>().Start();

using System.Diagnostics;
using System.IO;
using SDMS.Domain.Brain;

namespace SDMS.Infrastructure.Brain;

public sealed class BrainProcessManager : IDisposable
{
    private readonly string  _pythonExe;    // path to python.exe in your venv
    private readonly string  _apiScript;    // path to api.py
    private readonly string  _brainUrl;
    private readonly IBrainClient _client;
    private          Process? _process;

    /// <param name="pythonExe">
    ///   Full path to python.exe, e.g.
    ///   C:\Users\Omer\RiderProjects\SDMS\SDMS\Brain\.venv\Scripts\python.exe
    /// </param>
    /// <param name="apiScript">
    ///   Full path to api.py, e.g.
    ///   C:\Users\Omer\RiderProjects\SDMS\SDMS\Brain\api.py
    /// </param>
    /// <param name="brainUrl">Base URL — must match what BrainClient uses.</param>
    public BrainProcessManager(
        string pythonExe,
        string apiScript,
        string brainUrl = "http://127.0.0.1:5000")
    {
        _pythonExe = pythonExe;
        _apiScript  = apiScript;
        _brainUrl   = brainUrl;
        _client     = new BrainClient(brainUrl);
    }

    /// <summary>
    /// Start the Python brain server and wait until it responds to /status.
    /// Call this once at application startup before any AnalyzeAsync calls.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        // If already running from a previous session, don't start a second one
        if (await _client.IsAliveAsync(ct))
        {
            Console.WriteLine("[Brain] Already running.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName               = _pythonExe,
            Arguments              = $"\"{_apiScript}\"",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            // Run from the Brain directory so relative imports work
            WorkingDirectory       = Path.GetDirectoryName(_apiScript)!,
        };

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Python brain process.");

        // Optionally log stdout/stderr from Python to your app's console
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) Console.WriteLine($"[Brain] {e.Data}");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) Console.WriteLine($"[Brain ERR] {e.Data}");
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Console.WriteLine($"[Brain] Process started (PID {_process.Id}). Waiting for /status...");

        // Poll /status until the Flask server is up (up to 30 seconds)
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested) break;
            if (await _client.IsAliveAsync(ct))
            {
                Console.WriteLine("[Brain] Server is up.");
                return;
            }
            await Task.Delay(500, ct);
        }

        throw new TimeoutException(
            "Python brain server did not respond to /status within 30 seconds.");
    }

    /// <summary>Kill the Python process on app shutdown.</summary>
    public void Stop()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(3000);
            Console.WriteLine("[Brain] Process stopped.");
        }
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
        (_client as IDisposable)?.Dispose();
    }
}