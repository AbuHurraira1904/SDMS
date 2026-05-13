// BrainClient.cs  →  SDMS.Infrastructure/Brain/
// HTTP client that talks to the Python brain API.
// Sends the FileTree JSON path, gets back a PlanOutput.

using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SDMS.Domain.Brain;

namespace SDMS.Infrastructure.Brain;

public sealed class BrainClient : IBrainClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string     _baseUrl;

    public BrainClient(string baseUrl = "http://127.0.0.1:5000")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http    = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    /// <summary>Check the brain is running before starting a scan.</summary>
    public async Task<bool> IsAliveAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/status", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Send the FileTree JSON path to the brain and get back a PlanOutput.
    /// The brain reads the file itself — we only send the path string.
    /// </summary>
    public async Task<PlanOutput> AnalyzeAsync(
        string            fileTreeJsonPath,
        bool              readContent  = false,
        bool              allowHidden  = false,
        bool              allowSystem  = false,
        CancellationToken ct           = default)
    {
        var request = new
        {
            filetree_json_path = fileTreeJsonPath,
            read_content       = readContent,
            allow_hidden       = allowHidden,
            allow_system       = allowSystem,
        };

        var response = await _http.PostAsJsonAsync($"{_baseUrl}/analyze", request, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Brain returned {(int)response.StatusCode}: {error}");
        }

        var plan = await response.Content.ReadFromJsonAsync<PlanOutput>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        return plan ?? throw new InvalidDataException("Brain returned null plan.");
    }

    public void Dispose() => _http.Dispose();
}