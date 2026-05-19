using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class EditorFileServiceTests : IDisposable
{
    private readonly string _tempDir;

    public EditorFileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarEditorFileServiceTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            foreach (string path in Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_DoesNotUseTextFileReaderSizeLimitAsHardStop()
    {
        string path = Path.Combine(_tempDir, "large.txt");
        File.WriteAllText(path, new string('a', 10 * 1024 * 1024 + 8));

        var service = new EditorFileService(new AppSettings.EditorSettings { FileSizeLimitBytes = 0 });
        var session = service.Load(path);

        Assert.Equal(10 * 1024 * 1024 + 8, session.Document.Buffer.GetLine(0).Length);
    }

    [Fact]
    public void Save_PreservesUtf8BomAndCrLf()
    {
        string path = Path.Combine(_tempDir, "bom.txt");
        File.WriteAllText(path, "a\r\nb", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(path);
        session.MoveToDocumentEnd();
        session.InsertText("!");
        service.Save(session);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Contains("\r\n", File.ReadAllText(path, Encoding.UTF8), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_ConvertsLineEndingWhenFormatChanges()
    {
        string path = Path.Combine(_tempDir, "lf.txt");
        File.WriteAllText(path, "a\r\nb", new UTF8Encoding(false));

        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(path);
        session.Document.SetFormat(session.Document.Format.WithLineEnding(EditorLineEnding.Lf));
        service.Save(session);

        Assert.Equal("a\nb", File.ReadAllText(path, Encoding.UTF8));
    }
}
