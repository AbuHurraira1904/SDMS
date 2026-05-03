// ============================================================
// JSONTreeSerializer.cs  →  SDMS.Infrastructure/Serialization/
// ITreeSerializer_Interface implementation using System.Text.Json.
// Zero extra NuGet packages — ships with .NET 6+.
// ============================================================

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SDMS.Domain.Models;

namespace SDMS.Infrastructure.Serialization;

/// <summary>
/// Serialises and deserialises a <see cref="FileTree"/> as UTF-8 JSON.
/// Uses a 64 KB streaming FileStream to avoid large intermediate strings
/// for trees with many thousands of nodes.
/// </summary>
public sealed class JSONTreeSerializer : ITreeSerializer_Interface
{
    private readonly bool _compact;

    /// <param name="compact">
    /// <c>true</c>  → single-line compact JSON (smaller file, harder to read).
    /// <c>false</c> → pretty-printed with 2-space indent (default).
    /// </param>
    public JSONTreeSerializer(bool compact = false)
    {
        _compact = compact;
    }

    // Shared options — built once, reused for every call.
    private JsonSerializerOptions Options => new()
    {
        WriteIndented          = !_compact,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    /// <inheritdoc/>
    public async Task<string> SerializeAsync(
        FileTree tree, string outputPath, CancellationToken ct = default)
    {
        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        await using var stream = new FileStream(
            outputPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 65_536, useAsync: true);

        await JsonSerializer.SerializeAsync(stream, tree, Options, ct);
        return outputPath;
    }

    /// <inheritdoc/>
    public async Task<FileTree> DeserializeAsync(
        string inputPath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            inputPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 65_536, useAsync: true);

        var result = await JsonSerializer.DeserializeAsync<FileTree>(stream, Options, ct);
        return result ?? throw new InvalidDataException(
            $"Deserialisation returned null for '{inputPath}'.");
    }
}
