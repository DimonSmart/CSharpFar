using System.IO.Compression;
using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Module.Archives;

namespace CSharpFar.Tests;

public sealed class ArchiveEditorSaveTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(),
        "csharpfar-archive-editor-" + Guid.NewGuid().ToString("N"));

    public ArchiveEditorSaveTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string CreateArchive()
    {
        string path = Path.Combine(_folder, "edit.zip");
        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("doc.txt").Open(), new UTF8Encoding(false));
        writer.Write("original");
        return path;
    }

    private static LocalFilePanelSource Local() => new(new FileSystemService());

    [Fact]
    public async Task SaveAsync_UpdatesArchiveAndDocumentCleanState()
    {
        string path = CreateArchive();
        await using var archive = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(path), Path.GetFileName(path));
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(archive, "/doc.txt", "doc.txt");
        session.MoveToDocumentEnd();
        session.InsertText("-updated");

        await service.SaveAsync(session, archive, "/doc.txt");

        Assert.False(session.Document.IsDirty);
        await using var stream = await archive.OpenReadAsync("/doc.txt");
        using var reader = new StreamReader(stream);
        Assert.Equal("original-updated", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task SaveAsync_StaleArchiveRejectsAndPreservesDirtyDocument()
    {
        string path = CreateArchive();
        await using var editorSource = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(path), Path.GetFileName(path));
        await using var otherPanel = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(path), Path.GetFileName(path));
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(editorSource, "/doc.txt", "doc.txt");
        session.MoveToDocumentEnd();
        session.InsertText("-edit");

        await otherPanel.CreateDirectoryAsync("/external");
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(session, editorSource, "/doc.txt"));
        Assert.True(session.Document.IsDirty);
        Assert.NotNull(otherPanel.GetItem("/external"));
    }

    [Fact]
    public async Task SaveAsync_CreatesNewFileOnlyOnSave()
    {
        string path = CreateArchive();
        await using var archive = await ArchiveFilePanelSource.CreateAsync(
            Local(), PanelLocation.Local(path), Path.GetFileName(path));
        var service = new EditorFileService(new AppSettings.EditorSettings());
        var session = service.Load(archive, "/new.txt", "new.txt");
        session.InsertText("new");

        Assert.Null(archive.GetItem("/new.txt"));
        await service.SaveAsync(session, archive, "/new.txt");
        Assert.NotNull(archive.GetItem("/new.txt"));
    }
}
