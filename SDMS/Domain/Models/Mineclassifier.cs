namespace SDMS.Domain.Models;

/// <summary>
/// Static lookup to map file extensions to broad MIME categories.
/// Lookups are O(1) via an internal dictionary.
/// </summary>
public static class MineClassifier
{
    private static readonly IReadOnlyDictionary<string, string> ExtMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // -- Documents --
            ["pdf"]   = "document", ["doc"]   = "document", ["docx"]  = "document",
            ["odt"]   = "document", ["rtf"]   = "document", ["txt"]   = "document",
            ["md"]    = "document", ["rst"]   = "document", ["tex"]   = "document",
            ["xls"]   = "document", ["xlsx"]  = "document", ["ods"]   = "document",
            ["csv"]   = "document", ["ppt"]   = "document", ["pptx"]  = "document",
            ["epub"]  = "document", ["mobi"]  = "document",

            // -- Images --
            ["jpg"]   = "image",  ["jpeg"]  = "image",  ["png"]   = "image",
            ["gif"]   = "image",  ["bmp"]   = "image",  ["tiff"]  = "image",
            ["tif"]   = "image",  ["webp"]  = "image",  ["svg"]   = "image",
            ["ico"]   = "image",  ["heic"]  = "image",  ["psd"]   = "image",
            ["ai"]    = "image",  ["eps"]   = "image",

            // -- Audio --
            ["mp3"]   = "audio",  ["wav"]   = "audio",  ["flac"]  = "audio",
            ["aac"]   = "audio",  ["ogg"]   = "audio",  ["m4a"]   = "audio",
            ["wma"]   = "audio",  ["opus"]  = "audio",

            // -- Video --
            ["mp4"]   = "video",  ["mkv"]   = "video",  ["avi"]   = "video",
            ["mov"]   = "video",  ["wmv"]   = "video",  ["flv"]   = "video",
            ["webm"]  = "video",  ["m4v"]   = "video",

            // -- Code / Scripts --
            ["cs"]    = "code",   ["py"]    = "code",   ["js"]    = "code",
            ["ts"]    = "code",   ["jsx"]   = "code",   ["tsx"]   = "code",
            ["java"]  = "code",   ["c"]     = "code",   ["cpp"]   = "code",
            ["h"]     = "code",   ["hpp"]   = "code",   ["go"]    = "code",
            ["rs"]    = "code",   ["rb"]    = "code",   ["php"]   = "code",
            ["swift"] = "code",   ["kt"]    = "code",   ["sh"]    = "code",
            ["bash"]  = "code",   ["ps1"]   = "code",   ["bat"]   = "code",
            ["html"]  = "code",   ["css"]   = "code",   ["scss"]  = "code",
            ["sql"]   = "code",   ["json"]  = "code",   ["xml"]   = "code",
            ["yaml"]  = "code",   ["yml"]   = "code",   ["toml"]  = "code",
            ["ini"]   = "code",   ["proto"] = "code",   ["vue"]   = "code",
            ["svelte"]= "code",   ["dart"]  = "code",   ["fs"]    = "code",
            ["vb"]    = "code",   ["r"]     = "code",   ["lua"]   = "code",

            // -- Archives --
            ["zip"]   = "archive", ["tar"]  = "archive", ["gz"]   = "archive",
            ["bz2"]   = "archive", ["xz"]   = "archive", ["7z"]   = "archive",
            ["rar"]   = "archive", ["deb"]  = "archive", ["rpm"]  = "archive",
            ["apk"]   = "archive", ["iso"]  = "archive", ["dmg"]  = "archive",

            // -- Executables --
            ["exe"]   = "executable", ["dll"]   = "executable", ["so"]  = "executable",
            ["dylib"] = "executable", ["bin"]   = "executable", ["msi"] = "executable",

            // -- Data / Databases --
            ["db"]      = "data", ["sqlite"]  = "data", ["sqlite3"] = "data",
            ["parquet"] = "data", ["h5"]      = "data", ["log"]     = "data",
            ["pkl"]     = "data", ["msgpack"] = "data", ["npy"]     = "data",

            // -- Temp / Cache --
            ["tmp"]   = "temp",  ["bak"]   = "temp",  ["swp"]   = "temp",
            ["pyc"]   = "temp",  ["class"] = "temp",  ["o"]     = "temp",
            ["obj"]   = "temp",  ["cache"] = "temp",

            // -- System files --
            ["sys"]     = "system", ["drv"]  = "system", ["lnk"] = "system",
            ["inf"]     = "system", ["reg"]  = "system",
        };

    /// <summary>
    /// Maps extension (lowercase, no leading dot) to a category. 
    /// Returns "unknown" if no match is found.
    /// </summary>
    public static string Classify(string? extension)
    {
        if (string.IsNullOrEmpty(extension)) return "unknown";
        return ExtMap.TryGetValue(extension, out var cat) ? cat : "unknown";
    }

    /// <summary> Overload for processing a FileNode directly. </summary>
    public static string Classify(FileNode node) =>
        node.IsDirectory ? "directory" : Classify(node.Extension);
}