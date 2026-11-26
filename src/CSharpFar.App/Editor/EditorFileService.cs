using System.Text;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
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
        var destinationVersion = SafeLocalFilePublisher.CaptureDestination(Path.GetFullPath(filePath));
        if (!File.Exists(filePath))
        {
            var initialFormat = newFileFormat ?? CreateDefaultNewFileFormat(_settings);
            var newFileDocument = new EditorDocument(EditorTextBuffer.FromText(string.Empty), initialFormat);
            newFileDocument.MarkClean();
            return new EditorSession(filePath, newFileDocument, _settings, readOnly: false)
            { HasDestinationVersion = true, LocalDestinationVersion = destinationVersion };
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
        if (SafeLocalFilePublisher.CaptureDestination(Path.GetFullPath(filePath)) != destinationVersion)
            throw new IOException("The file changed while being opened.");
        return new EditorSession(filePath, document, _settings, readOnly)
        { HasDestinationVersion = true, LocalDestinationVersion = destinationVersion };
    }

    public Task<bool> RequiresSizeWarningAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => RequiresSizeWarning(path), cancellationToken);

    public async Task<bool> RequiresSizeWarningAsync(IFilePanelSource source, string path, CancellationToken cancellationToken = default)
    {
        if (_settings.FileSizeLimitBytes <= 0) return false;
        var item = await source.AsAsync().GetItemAsync(path, cancellationToken).ConfigureAwait(false);
        return item is { IsDirectory: false, Size: long size } && size > _settings.FileSizeLimitBytes;
    }

    public Task<EditorSession> LoadAsync(string path, EditorDocumentFormat? newFileFormat = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(path, newFileFormat), cancellationToken);

    public EditorSession Load(IFilePanelSource source, string sourcePath, string displayPath,
        EditorDocumentFormat? newFileFormat = null) =>
        LoadAsync(source, sourcePath, displayPath, newFileFormat).GetAwaiter().GetResult();

    public async Task<EditorSession> LoadAsync(
        IFilePanelSource source,
        string sourcePath,
        string displayPath,
        EditorDocumentFormat? newFileFormat = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var item = await source.AsAsync().GetItemAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            var initialFormat = newFileFormat ?? CreateDefaultNewFileFormat(_settings);
            var newFileDocument = new EditorDocument(EditorTextBuffer.FromText(string.Empty), initialFormat);
            newFileDocument.MarkClean();
            return new EditorSession(displayPath, newFileDocument, _settings, readOnly: false)
            { HasDestinationVersion = true, ProviderDestinationVersion = null };
        }

        if (item.IsDirectory)
            throw new IOException("Cannot open a directory in the editor.");

        byte[] bytes;
        await using (var stream = await source.AsAsync().OpenReadAsync(sourcePath, cancellationToken).ConfigureAwait(false))
        using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
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
        return new EditorSession(displayPath, document, _settings, readOnly)
        { HasDestinationVersion = true, ProviderDestinationVersion = item };
    }

    public void Save(EditorSession session) =>
        ApplySaveResult(session, WriteAsync(session).GetAwaiter().GetResult());

    public void Save(EditorSession session, IFilePanelSource source, string sourcePath) =>
        SaveAsync(session, source, sourcePath).GetAwaiter().GetResult();

    public async Task SaveAsync(EditorSession session, IFilePanelSource source, string sourcePath,
        CancellationToken cancellationToken = default) =>
        ApplySaveResult(session, await WriteAsync(session, source, sourcePath, cancellationToken).ConfigureAwait(false));

    // Capture editor state on its owning thread; only immutable data goes to the worker.
    internal Task<SaveResult> WriteAsync(EditorSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureWritable(session);
        var bytes = Encode(session);
        var path = session.FilePath;
        var hasExpected = session.HasDestinationVersion;
        var expected = session.LocalDestinationVersion;
        return Task.Run(async () =>
        {
            if (!hasExpected) expected = SafeLocalFilePublisher.CaptureDestination(Path.GetFullPath(path));
            var warning = await SafeFileWriteService.WriteLocalAsync(path, bytes, expected, cancellationToken).ConfigureAwait(false);
            try
            {
                return new SaveResult(warning, SafeLocalFilePublisher.CaptureDestination(Path.GetFullPath(path)), null, false);
            }
            catch (Exception error) { return new SaveResult(RefreshWarning(warning, error), null, null, true); }
        }, cancellationToken);
    }

    internal Task<SaveResult> WriteAsync(EditorSession session, IFilePanelSource source, string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        EnsureWritable(session);
        var bytes = Encode(session);
        var hasExpected = session.HasDestinationVersion;
        var expected = session.ProviderDestinationVersion;
        return Task.Run(async () =>
        {
            if (!hasExpected) expected = await source.AsAsync().GetItemAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var warning = await SafeFileWriteService.WriteDocumentAsync(source, sourcePath, bytes, expected, cancellationToken).ConfigureAwait(false);
            try
            {
                var version = await source.AsAsync().GetItemAsync(sourcePath, CancellationToken.None).ConfigureAwait(false);
                return new SaveResult(warning, null, version, false);
            }
            catch (Exception error) { return new SaveResult(RefreshWarning(warning, error), null, null, true); }
        }, cancellationToken);
    }

    internal sealed record SaveResult(string? Warning, SafeLocalFilePublisher.DestinationSnapshot? LocalVersion,
        FilePanelItem? ProviderVersion, bool VersionInvalid);

    private static string RefreshWarning(string? warning, Exception error) =>
        string.Join(Environment.NewLine, new[] { warning,
            "File saved, but the destination version could not be refreshed: " + error.Message }.Where(message => message is not null));

    internal static void ApplySaveResult(EditorSession session, SaveResult result)
    {
        session.DestinationVersionInvalid = result.VersionInvalid;
        if (!result.VersionInvalid)
        {
            session.HasDestinationVersion = true;
            session.LocalDestinationVersion = result.LocalVersion;
            session.ProviderDestinationVersion = result.ProviderVersion;
        }
        session.LastSaveWarning = result.Warning;
        session.Document.MarkClean();
        session.RaiseSaved();
    }

    private static void EnsureWritable(EditorSession session)
    {
        if (session.ReadOnly)
            throw new IOException("The file is opened read-only.");
        if (session.DestinationVersionInvalid)
            throw new IOException("Cannot verify the saved file version. Reopen the document before saving again.");
    }

    private static byte[] Encode(EditorSession session)
    {
        Encoding encoding = session.Document.Format.CreateSaveEncoding();
        byte[] content = encoding.GetBytes(session.Document.Buffer.GetText(session.Document.Format));
        return [.. encoding.GetPreamble(), .. content];
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
