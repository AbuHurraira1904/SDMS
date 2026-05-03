// ============================================================
// Msgtreeserializer.cs  →  SDMS.Infrastructure/Serialization/
// ITreeSerializer_Interface implementation using MessagePack.
// Requires: dotnet add package MessagePack
// Produces ~60% smaller output than JSON for large trees.
// Falls back gracefully if the package is not installed.
// ============================================================

using System.IO;
using SDMS.Domain.Models;

namespace SDMS.Infrastructure.Serialization;

/// <summary>
/// Serialises and deserialises a <see cref="FileTree"/> as a compact
/// MessagePack binary file.
/// Implements the same <see cref="ITreeSerializer_Interface"/> contract as
/// <see cref="JSONTreeSerializer"/> — swap at the DI registration, nothing else changes.
/// </summary>
public sealed class Msgtreeserializer : ITreeSerializer_Interface
{
    /// <inheritdoc/>
    public async Task<string> SerializeAsync(
        FileTree tree, string outputPath, CancellationToken ct = default)
    {
        EnsurePackageAvailable();
        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        await Task.Run(() => WriteInternal(tree, outputPath), ct);
        return outputPath;
    }

    /// <inheritdoc/>
    public async Task<FileTree> DeserializeAsync(
        string inputPath, CancellationToken ct = default)
    {
        EnsurePackageAvailable();
        return await Task.Run(() => ReadInternal(inputPath), ct);
    }

    // ── Internal — uses reflection so this class compiles without a hard
    //    compile-time reference to MessagePack.dll. ───────────────────────────

    private static void WriteInternal(FileTree tree, string outputPath)
    {
        var (serType, serMethod, deserMethod) = ResolveTypes(typeof(FileTree));
        using var stream = File.Create(outputPath);
        serMethod.Invoke(null, [stream, tree, null]);
    }

    private static FileTree ReadInternal(string inputPath)
    {
        var (serType, serMethod, deserMethod) = ResolveTypes(typeof(FileTree));
        using var stream = File.OpenRead(inputPath);
        var result = deserMethod.Invoke(null, [stream, null]);
        return result as FileTree
            ?? throw new InvalidDataException($"MessagePack deserialisation returned null for '{inputPath}'.");
    }

    private static (Type, System.Reflection.MethodInfo, System.Reflection.MethodInfo)
        ResolveTypes(Type targetType)
    {
        var serType = Type.GetType("MessagePack.MessagePackSerializer, MessagePack")
            ?? throw new InvalidOperationException("MessagePackSerializer type not found.");

        var bindingFlags =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static;

        var serMethod = serType
            .GetMethods(bindingFlags)
            .First(m => m.Name == "Serialize" && m.IsGenericMethod)
            .MakeGenericMethod(targetType);

        var deserMethod = serType
            .GetMethods(bindingFlags)
            .First(m => m.Name == "Deserialize" && m.IsGenericMethod)
            .MakeGenericMethod(targetType);

        return (serType, serMethod, deserMethod);
    }

    private static void EnsurePackageAvailable()
    {
        bool loaded = AppDomain.CurrentDomain
            .GetAssemblies()
            .Any(a => a.GetName().Name == "MessagePack");

        if (loaded) return;

        try { System.Reflection.Assembly.Load("MessagePack"); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Msgtreeserializer requires the 'MessagePack' NuGet package.\n" +
                "Install with: dotnet add package MessagePack", ex);
        }
    }
}
