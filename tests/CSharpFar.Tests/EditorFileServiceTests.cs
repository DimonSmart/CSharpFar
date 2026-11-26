using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Tests.Infrastructure;
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
    public void ProviderSave_StreamDisposeFailureLeavesDocumentDirty()
    {
        var source = new InMemoryBatchFilePanelSource { FailWrite = true };
        source.SeedFile("/document.txt", "old");
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(source, "/document.txt", "/document.txt");
        session.MoveToDocumentEnd();
        session.InsertText(" new");
        Assert.True(session.Document.IsDirty);

        Assert.Throws<IOException>(() => service.Save(session, source, "/document.txt"));
        Assert.True(session.Document.IsDirty);
        Assert.Equal("old", source.ReadText("/document.txt"));
    }

    [Fact]
    public async Task ProviderSaveAsync_FailedBatchCommitKeepsDirtyDocumentAndOldContent()
    {
        var source = new InMemoryBatchFilePanelSource { FailCommit = true };
        source.SeedFile("/document.txt", "old");
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(source, "/document.txt", "/document.txt");
        session.MoveToDocumentEnd();
        session.InsertText(" new");

        await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(session, source, "/document.txt"));

        Assert.True(session.Document.IsDirty);
        Assert.Equal("old", source.ReadText("/document.txt"));
        Assert.Equal(0, source.Committed);
        Assert.Equal(1, source.Aborted);
    }

    [Fact]
    public async Task ProviderSaveAsync_SuccessfulBatchCommitCleansDocument()
    {
        var source = new InMemoryBatchFilePanelSource();
        source.SeedFile("/document.txt", "old");
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(source, "/document.txt", "/document.txt");
        session.MoveToDocumentEnd();
        session.InsertText(" new");

        await service.SaveAsync(session, source, "/document.txt");
        Assert.False(session.Document.IsDirty);
        Assert.Equal("old new", source.ReadText("/document.txt"));
        Assert.Equal(1, source.Committed);
    }

    [Fact]
    public async Task ProviderSaveAsync_PostCommitCleanupFailureIsWarning()
    {
        var source = new InMemoryBatchFilePanelSource { FailDisposeAfterCommit = true };
        source.SeedFile("/document.txt", "old");
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(source, "/document.txt", "/document.txt");
        session.MoveToDocumentEnd();
        session.InsertText(" new");

        await service.SaveAsync(session, source, "/document.txt");

        Assert.False(session.Document.IsDirty);
        Assert.NotNull(session.LastSaveWarning);
        Assert.Contains("cleanup failure", session.LastSaveWarning);
        Assert.Equal("old new", source.ReadText("/document.txt"));
        Assert.Equal(1, source.Committed);
    }

    [Fact]
    public void Save_NewFileCreatedAfterOpeningIsNotOverwritten()
    {
        string path = Path.Combine(_tempDir, "new.txt");
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(path);
        session.InsertText("editor");
        File.WriteAllText(path, "external");
        Assert.Throws<IOException>(() => service.Save(session));
        Assert.True(session.Document.IsDirty);
        Assert.Equal("external", File.ReadAllText(path));
    }

    [Fact]
    public void Save_NewFilePublishesCompleteContent()
    {
        string path = Path.Combine(_tempDir, "new.txt");
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(path);
        session.InsertText("editor");
        service.Save(session);
        Assert.False(session.Document.IsDirty);
        Assert.Equal("editor", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public void Save_ExternallyChangedFileIsNotOverwritten()
    {
        string path = Path.Combine(_tempDir, "changed.txt");
        File.WriteAllText(path, "old");
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(path);
        session.MoveToDocumentEnd();
        session.InsertText(" edit");
        File.WriteAllText(path, "external update");
        Assert.Throws<IOException>(() => service.Save(session));
        Assert.True(session.Document.IsDirty);
        Assert.Equal("external update", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_ProviderWithoutSafePublicationUsesBestEffort(bool fail)
    {
        var source = new MemoryFilePanelView(new PanelSourceId("unverified"));
        source.SeedFile("/doc", "old");
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(source, "/doc", "/doc");
        session.MoveToDocumentEnd();
        session.InsertText(" edit");
        source.FailWrite = fail;
        if (fail)
            await Assert.ThrowsAsync<CSharpFar.FileSystem.UnknownFilePublicationException>(() => service.SaveAsync(session, source, "/doc"));
        else
            await service.SaveAsync(session, source, "/doc");
        Assert.Equal(fail, session.Document.IsDirty);
        Assert.Equal(fail ? "old" : "old edit", source.ReadText("/doc"));
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
    public void Load_StripsDetectedUtf8BomFromDocumentText()
    {
        string path = Path.Combine(_tempDir, "bom-json.txt");
        File.WriteAllText(path, "{\"items\":[1,2]}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(path);

        Assert.True(session.Document.Format.EmitByteOrderMark);
        Assert.Equal("UTF-8", session.Document.Format.EncodingDisplayName);
        Assert.Equal("BOM", session.Document.Format.BomDisplayName);
        Assert.Equal("{\"items\":[1,2]}", session.Document.Buffer.GetLine(0));
        Assert.False(session.Document.Buffer.GetLine(0).StartsWith('\uFEFF'));
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
