using System.Text;
using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 12: TextFileReader encoding detection and line reading.
/// </summary>
public class FileViewerTests : IDisposable
{
    private readonly string _tempDir;

    public FileViewerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarViewerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Encoding detection ────────────────────────────────────────────────────

    [Fact]
    public void ReadLines_ReadsPlainUtf8()
    {
        string path = Write("utf8.txt", "Line 1\nLine 2\nLine 3", Encoding.UTF8);

        string[] lines = TextFileReader.ReadLines(path);

        Assert.Equal(3, lines.Length);
        Assert.Equal("Line 1", lines[0]);
        Assert.Equal("Line 3", lines[2]);
    }

    [Fact]
    public void ReadLines_HandlesUtf8Bom()
    {
        string path = WritePath("bom.txt");
        File.WriteAllText(path, "Hello BOM", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        string[] lines = TextFileReader.ReadLines(path);

        Assert.Single(lines);
        Assert.Equal("Hello BOM", lines[0]); // BOM stripped by StreamReader
    }

    [Fact]
    public void ReadLines_FallsBackForNonUtf8()
    {
        // 0xE9 = é in Windows-1252, invalid as standalone UTF-8 byte
        string path = WritePath("ansi.txt");
        File.WriteAllBytes(path, [0xE9, 0x0D, 0x0A]); // é + CRLF

        // Must not throw
        string[] lines = TextFileReader.ReadLines(path);
        Assert.Single(lines);
    }

    [Fact]
    public void ReadLines_ReturnsEmptyForEmptyFile()
    {
        string path = Write("empty.txt", "", Encoding.UTF8);

        string[] lines = TextFileReader.ReadLines(path);

        Assert.Empty(lines);
    }

    [Fact]
    public void ReadLines_PreservesLineCount()
    {
        string content = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line{i}"));
        string path = Write("ten.txt", content, Encoding.UTF8);

        string[] lines = TextFileReader.ReadLines(path);

        Assert.Equal(10, lines.Length);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string WritePath(string name) => Path.Combine(_tempDir, name);

    private string Write(string name, string content, Encoding enc)
    {
        string path = WritePath(name);
        File.WriteAllText(path, content, enc);
        return path;
    }
}
