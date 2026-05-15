// ============================================================
// MimeclassifierTests.cs  →  SDMS.Tests/Models/
// Tests for SDMS.Domain.Models.Mimeclassifier
//
// What we're testing:
//   1. Known extensions return the right category
//   2. Lookup is case-insensitive
//   3. Unknown extension returns "unknown"
//   4. Null or empty extension returns "unknown"
//   5. Directory FileNode returns "directory" via the overload
//   6. File FileNode returns the correct category via the overload
//   7. Extensions across every declared category have at least one entry
// ============================================================

using System.IO;
using SDMS.Domain.Models;
using SDMS.Tests.Helpers;
using Xunit;

namespace SDMS.Tests.Models;

public sealed class MimeclassifierTests
{
    // ── 1. Known extensions ───────────────────────────────────────────────────

    [Theory]
    [InlineData("pdf",    "document")]
    [InlineData("docx",   "document")]
    [InlineData("txt",    "document")]
    [InlineData("xlsx",   "document")]
    [InlineData("jpg",    "image")]
    [InlineData("png",    "image")]
    [InlineData("svg",    "image")]
    [InlineData("mp3",    "audio")]
    [InlineData("flac",   "audio")]
    [InlineData("mp4",    "video")]
    [InlineData("mkv",    "video")]
    [InlineData("cs",     "code")]
    [InlineData("py",     "code")]
    [InlineData("json",   "code")]
    [InlineData("zip",    "archive")]
    [InlineData("rar",    "archive")]
    [InlineData("7z",     "archive")]
    [InlineData("exe",    "executable")]
    [InlineData("dll",    "executable")]
    [InlineData("db",     "data")]
    [InlineData("sqlite", "data")]
    [InlineData("log",    "data")]
    [InlineData("tmp",    "temp")]
    [InlineData("bak",    "temp")]
    [InlineData("pyc",    "temp")]
    [InlineData("sys",    "system")]
    [InlineData("reg",    "system")]
    public void Classify_KnownExtension_ReturnsCorrectCategory(string ext, string expected)
    {
        Assert.Equal(expected, Mimeclassifier.Classify(ext));
    }

    // ── 2. Case-insensitive lookup ────────────────────────────────────────────

    [Theory]
    [InlineData("PDF")]
    [InlineData("Pdf")]
    [InlineData("pDf")]
    public void Classify_Extension_IsCaseInsensitive(string ext)
    {
        Assert.Equal("document", Mimeclassifier.Classify(ext));
    }

    // ── 3. Unknown extension ──────────────────────────────────────────────────

    [Theory]
    [InlineData("zzz")]
    [InlineData("xyz")]
    [InlineData("madeup")]
    public void Classify_UnknownExtension_ReturnsUnknown(string ext)
    {
        Assert.Equal("unknown", Mimeclassifier.Classify(ext));
    }

    // ── 4. Null or empty ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Classify_NullOrEmptyExtension_ReturnsUnknown(string? ext)
    {
        Assert.Equal("unknown", Mimeclassifier.Classify(ext));
    }

    // ── 5. Directory node overload ────────────────────────────────────────────

    [Fact]
    public void Classify_DirectoryNode_ReturnsDirectory()
    {
        var dir = Builders.Dir("SomeFolder");
        Assert.Equal("directory", Mimeclassifier.Classify(dir));
    }

    // ── 6. File node overload ─────────────────────────────────────────────────

    [Fact]
    public void Classify_FileNode_ReturnsCorrectCategory()
    {
        var file = Builders.File("report.pdf");
        Assert.Equal("document", Mimeclassifier.Classify(file));
    }

    [Fact]
    public void Classify_FileNodeWithUnknownExtension_ReturnsUnknown()
    {
        var file = Builders.File("weird.zzz");
        Assert.Equal("unknown", Mimeclassifier.Classify(file));
    }

    // ── 7. Every category has at least one registered extension ───────────────

    [Theory]
    [InlineData("document")]
    [InlineData("image")]
    [InlineData("audio")]
    [InlineData("video")]
    [InlineData("code")]
    [InlineData("archive")]
    [InlineData("executable")]
    [InlineData("data")]
    [InlineData("temp")]
    [InlineData("system")]
    public void Classify_EachCategory_HasAtLeastOneKnownExtension(string category)
    {
        // Probe a known representative for each category.
        // If the classifier grows, these tests will still pass.
        var representatives = new Dictionary<string, string>
        {
            ["document"]   = "pdf",
            ["image"]      = "png",
            ["audio"]      = "mp3",
            ["video"]      = "mp4",
            ["code"]       = "cs",
            ["archive"]    = "zip",
            ["executable"] = "exe",
            ["data"]       = "db",
            ["temp"]       = "tmp",
            ["system"]     = "sys",
        };

        var ext    = representatives[category];
        var result = Mimeclassifier.Classify(ext);

        Assert.Equal(category, result);
    }
}
