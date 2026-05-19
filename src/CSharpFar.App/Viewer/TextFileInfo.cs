using System.Text;
using System.Runtime.Versioning;
using CSharpFar.Core.Text;

namespace CSharpFar.App.Viewer;

/// <summary>
/// Detects BOM, line ending style, and associated application for a text file.
/// </summary>
internal sealed class TextFileInfo
{
    public string? BomName    { get; private init; }
    public string  LineEnding { get; private init; } = string.Empty;
    public string? AppName    { get; private init; }

    // ── text extensions ───────────────────────────────────────────────────────

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".markdown", ".rst", ".csv", ".tsv",
        ".xml", ".xaml", ".html", ".htm", ".xhtml", ".svg",
        ".json", ".jsonc", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".properties",
        ".cs", ".vb", ".fs", ".fsx", ".csx",
        ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp",
        ".java", ".kt", ".scala",
        ".py", ".rb", ".pl", ".php", ".lua", ".r",
        ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs",
        ".css", ".scss", ".less",
        ".sh", ".bash", ".zsh", ".fish", ".ps1", ".bat", ".cmd",
        ".sql", ".graphql",
        ".go", ".rs", ".swift", ".dart",
        ".gitignore", ".gitattributes", ".editorconfig", ".env",
        ".csproj", ".vbproj", ".fsproj", ".sln", ".props", ".targets",
        ".resx", ".config", ".nuspec",
        ".dockerfile", ".tf", ".hcl",
    };

    public static bool IsTextFile(string path) =>
        TextExtensions.Contains(Path.GetExtension(path));

    // ── factory ───────────────────────────────────────────────────────────────

    public static TextFileInfo Read(string path)
    {
        string? bomName    = null;
        string  lineEnding = string.Empty;
        string? appName    = null;

        try
        {
            using var fs = File.OpenRead(path);

            // BOM detection
            byte[] buf = new byte[4];
            int read = fs.Read(buf, 0, 4);
            bomName = DetectBom(buf, read);

            // Line ending detection (read up to first 64 KB of content)
            fs.Seek(0, SeekOrigin.Begin);
            long sampleLen = Math.Min(fs.Length, 65536);
            byte[] sample = new byte[sampleLen];
            int sampleRead = fs.Read(sample, 0, (int)sampleLen);
            lineEnding = TextLineEndingDetector.DisplayName(
                TextLineEndingDetector.DetectBytes(sample.AsSpan(0, sampleRead)));
        }
        catch { }

        try
        {
            if (OperatingSystem.IsWindows())
                appName = GetAssociatedApp(path);
        }
        catch { }

        return new TextFileInfo { BomName = bomName, LineEnding = lineEnding, AppName = appName };
    }

    // ── BOM detection ─────────────────────────────────────────────────────────

    private static string? DetectBom(byte[] buf, int len)
    {
        if (len >= 4 && buf[0] == 0x00 && buf[1] == 0x00 && buf[2] == 0xFE && buf[3] == 0xFF)
            return "UTF-32 BE";
        if (len >= 4 && buf[0] == 0xFF && buf[1] == 0xFE && buf[2] == 0x00 && buf[3] == 0x00)
            return "UTF-32 LE";
        if (len >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
            return "UTF-8";
        if (len >= 2 && buf[0] == 0xFF && buf[1] == 0xFE)
            return "UTF-16 LE";
        if (len >= 2 && buf[0] == 0xFE && buf[1] == 0xFF)
            return "UTF-16 BE";
        return null;
    }

    // ── associated application ────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static string? GetAssociatedApp(string path)
    {
        string ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return null;

        using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ext);
        string? progId = key?.GetValue(null) as string;
        if (progId is null) return null;

        using var openCmd = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(
            $@"{progId}\shell\open\command");
        string? cmd = openCmd?.GetValue(null) as string;
        if (cmd is null) return null;

        // Extract just the executable name
        cmd = cmd.Trim('"');
        int end = cmd.IndexOf('"');
        if (end > 0) cmd = cmd[..end];
        string exe = Path.GetFileNameWithoutExtension(cmd.Split(' ')[0]);
        return exe.Length > 0 ? exe : null;
    }
}
