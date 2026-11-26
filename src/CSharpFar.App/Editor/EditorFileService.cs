using System.Text;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Text;
using CSharpFar.FileSystem;

namespace CSharpFar.App.Editor;

public sealed class EditorFileService
{
    private const int MaxEncodingSampleBytes = 64 * 1024;

    private readonly AppSettings.EditorSettings _settings;

    public EditorFileService(AppSettings.EditorSettings settings)
    {
        _settings = settings;
    }

    public bool RequiresSizeWarning(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        long limit = _settings.FileSizeLimitBytes;
        return limit > 0 && new FileInfo(filePath).Length > limit;
    }

    public bool RequiresSizeWarning(IFilePanelSource source, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(source);

        var item = source.GetItem(sourcePath);
        if (item is null || item.IsDirectory || item.Size is null)
            return false;

        long limit = _settings.FileSizeLimitBytes;
        return limit > 0 && item.Size.Value > limit;
    }

    public EditorSession Load(string filePath, EditorDocumentFormat? newFileFormat = null)
    {
        if (!File.Exists(filePath))
        {
            var initialFormat = newFileFormat ?? CreateDefaultNewFileFormat(_settings);
            var newFileDocument = new EditorDocument(EditorTextBuffer.FromText(string.Empty), initialFormat);
            newFileDocument.MarkClean();
            return new EditorSession(filePath, newFileDocument, _settings, readOnly: false);
        }

        byte[] sample = ReadSample(filePath);
        var detection = TextEncodingDetector.Detect(sample);
        byte[] bytes = File.ReadAllBytes(filePath);
        string text = detection.Encoding.GetString(
            bytes,
            detection.ContentStartLength,
            bytes.Length - detection.ContentStartLength);

        var lineEnding = ToEditorLineEnding(TextLineEndingDetector.Detect(text));
        var format = new EditorDocumentFormat(
            detection.Encoding,
            detection.HasByteOrderMark,
            lineEnding == EditorLineEnding.Mixed
                ? EditorLineEnding.Mixed
                : lineEnding,
            detection.DisplayName);
        var document = new EditorDocument(EditorTextBuffer.FromText(text), format);
        document.MarkClean();

        bool readOnly = File.GetAttributes(filePath).HasFlag(FileAttributes.ReadOnly) &&
            _settings.OpenReadOnlyFilesReadOnly;
        return new EditorSession(filePath, document, _settings, readOnly);
    }

    public EditorSession Load(
        IFilePanelSource source,
        string sourcePath,
        string displayPath,
        EditorDocumentFormat? newFileFormat = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var item = source.GetItem(sourcePath);
        if (item is null)
        {
            var initialFormat = newFileFormat ?? CreateDefaultNewFileFormat(_settings);
            var newFileDocument = new EditorDocument(EditorTextBuffer.FromText(string.Empty), initialFormat);
            newFileDocument.MarkClean();
            return new EditorSession(displayPath, newFileDocument, _settings, readOnly: false);
        }

        if (item.IsDirectory)
            throw new IOException("Cannot open a directory in the editor.");

        byte[] bytes;
        using (var stream = source.OpenReadAsync(sourcePath).GetAwaiter().GetResult())
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            bytes = memory.ToArray();
        }

        byte[] sample = bytes.Length <= MaxEncodingSampleBytes ? bytes : bytes[..MaxEncodingSampleBytes];
        var detection = TextEncodingDetector.Detect(sample);
        string text = detection.Encoding.GetString(
            bytes,
            detection.ContentStartLength,
            bytes.Length - detection.ContentStartLength);

        var lineEnding = ToEditorLineEnding(TextLineEndingDetector.Detect(text));
        var format = new EditorDocumentFormat(
            detection.Encoding,
            detection.HasByteOrderMark,
            lineEnding == EditorLineEnding.Mixed
                ? EditorLineEnding.Mixed
                : lineEnding,
            detection.DisplayName);
        var document = new EditorDocument(EditorTextBuffer.FromText(text), format);
        document.MarkClean();

        bool readOnly = item.Attributes.HasFlag(FileAttributes.ReadOnly) &&
            _settings.OpenReadOnlyFilesReadOnly;
        return new EditorSession(displayPath, document, _settings, readOnly);
    }

    public void Save(EditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureWritable(session);
        string? warning = SafeFileWriteService.WriteLocalAsync(session.FilePath, Encode(session))
            .GetAwaiter().GetResult();
        CompleteSave(session, warning);
    }

    public void Save(EditorSession session, IFilePanelSource source, string sourcePath) =>
        SaveAsync(session, source, sourcePath).GetAwaiter().GetResult();

    public async Task SaveAsync(
        EditorSession session, IFilePanelSource source, string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        EnsureWritable(session);
        string? warning = await SafeFileWriteService.WriteDocumentAsync(
            source, sourcePath, Encode(session), cancellationToken).ConfigureAwait(false);
        CompleteSave(session, warning);
    }

    private static void EnsureWritable(EditorSession session)
    {
        if (session.ReadOnly)
            throw new IOException("The file is opened read-only.");
    }

    private static byte[] Encode(EditorSession session)
    {
        Encoding encoding = session.Document.Format.CreateSaveEncoding();
        byte[] content = encoding.GetBytes(session.Document.Buffer.GetText(session.Document.Format));
        return [.. encoding.GetPreamble(), .. content];
    }

    private static void CompleteSave(EditorSession session, string? warning)
    {
        session.LastSaveWarning = warning;
        session.Document.MarkClean();
        session.RaiseSaved();
    }

    public static EditorLineEnding ToEditorLineEnding(TextLineEndingKind kind) =>
        kind switch
        {
            TextLineEndingKind.CrLf => EditorLineEnding.CrLf,
            TextLineEndingKind.Cr => EditorLineEnding.Cr,
            TextLineEndingKind.Mixed => EditorLineEnding.Mixed,
            _ => EditorLineEnding.Lf,
        };

    public static TextLineEndingKind ToTextLineEndingKind(EditorLineEnding lineEnding) =>
        lineEnding switch
        {
            EditorLineEnding.CrLf => TextLineEndingKind.CrLf,
            EditorLineEnding.Cr => TextLineEndingKind.Cr,
            EditorLineEnding.Mixed => TextLineEndingKind.Mixed,
            _ => TextLineEndingKind.Lf,
        };

    public static EditorDocumentFormat CreateDefaultNewFileFormat(AppSettings.EditorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return new EditorDocumentFormat(
            encoding,
            emitByteOrderMark: false,
            EditorSettingsResolver.ResolveDefaultLineEnding(settings),
            "UTF-8");
    }

    private static byte[] ReadSample(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        int sampleLength = (int)Math.Min(stream.Length, MaxEncodingSampleBytes);
        var sample = new byte[sampleLength];
        int totalRead = 0;
        while (totalRead < sample.Length)
        {
            int read = stream.Read(sample, totalRead, sample.Length - totalRead);
            if (read == 0)
                break;

            totalRead += read;
        }

        if (totalRead == sample.Length)
            return sample;

        Array.Resize(ref sample, totalRead);
        return sample;
    }
}
