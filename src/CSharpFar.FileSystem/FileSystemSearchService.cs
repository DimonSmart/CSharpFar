using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.FileMasks;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class FileSystemSearchService : ISearchService
{
    private const int ResultBufferSize = 128;
    private const int FileReadBufferSize = 81920;
    private const int BinarySampleSize = 4096;
    private const int PreviewMaxLength = 160;

    static FileSystemSearchService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async IAsyncEnumerable<SearchResultItem> SearchAsync(
        SearchRequest request,
        IProgress<SearchProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalizedRequest = NormalizeRequest(request);
        ValidateRequest(normalizedRequest);

        var resultChannel = Channel.CreateBounded<SearchResultItem>(
            new BoundedChannelOptions(ResultBufferSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });

        int fileBufferSize = Math.Max(16, normalizedRequest.MaxDegreeOfParallelism * 4);
        var fileChannel = Channel.CreateBounded<FileSearchCandidate>(
            new BoundedChannelOptions(fileBufferSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true,
            });

        var progressState = new SearchProgressState(progress);

        Task producer = Task.Run(
            () => ProduceCandidatesAsync(
                normalizedRequest,
                resultChannel.Writer,
                fileChannel.Writer,
                progressState,
                cancellationToken),
            CancellationToken.None);

        Task[] workers = normalizedRequest.ContainingText is null
            ? []
            : Enumerable.Range(0, normalizedRequest.MaxDegreeOfParallelism)
                .Select(_ => Task.Run(
                    () => ProcessFileCandidatesAsync(
                        normalizedRequest,
                        fileChannel.Reader,
                        resultChannel.Writer,
                        progressState,
                        cancellationToken),
                    CancellationToken.None))
                .ToArray();

        Task completion = CompleteSearchAsync(producer, workers, resultChannel.Writer);

        await foreach (var item in resultChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;

        await completion.ConfigureAwait(false);
    }

    private static SearchRequest NormalizeRequest(SearchRequest request)
    {
        string mask = string.IsNullOrWhiteSpace(request.FileMaskExpression)
            ? "*"
            : request.FileMaskExpression.Trim();

        string? containingText = string.IsNullOrEmpty(request.ContainingText)
            ? null
            : request.ContainingText;

        return request with
        {
            RootPath = Path.GetFullPath(request.RootPath),
            FileMaskExpression = mask,
            ContainingText = containingText,
            NotContaining = containingText is not null && request.NotContaining,
        };
    }

    private static void ValidateRequest(SearchRequest request)
    {
        if (!Directory.Exists(request.RootPath))
            throw new DirectoryNotFoundException($"Search root does not exist: {request.RootPath}");

        if (request.MaxDegreeOfParallelism is < 1 or > 16)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaxDegreeOfParallelism,
                "Search parallelism must be in the range 1..16.");

        if (request.MaxContentSearchFileSizeBytes < 0)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaxContentSearchFileSizeBytes,
                "Maximum content search file size must not be negative.");

        if (request.EncodingMode != SearchEncodingMode.Automatic)
            throw new ArgumentOutOfRangeException(nameof(request), request.EncodingMode, "Unsupported search encoding mode.");
    }

    private static async Task ProduceCandidatesAsync(
        SearchRequest request,
        ChannelWriter<SearchResultItem> resultWriter,
        ChannelWriter<FileSearchCandidate> fileWriter,
        SearchProgressState progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await TraverseDirectoriesAsync(request, resultWriter, fileWriter, progress, cancellationToken)
                .ConfigureAwait(false);
            fileWriter.TryComplete();
        }
        catch (Exception ex)
        {
            fileWriter.TryComplete(ex);
            throw;
        }
    }

    private static async Task CompleteSearchAsync(
        Task producer,
        IReadOnlyList<Task> workers,
        ChannelWriter<SearchResultItem> resultWriter)
    {
        Exception? failure = null;

        try
        {
            await producer.ConfigureAwait(false);
            if (workers.Count > 0)
                await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        resultWriter.TryComplete(failure);
    }

    private static async Task TraverseDirectoriesAsync(
        SearchRequest request,
        ChannelWriter<SearchResultItem> resultWriter,
        ChannelWriter<FileSearchCandidate> fileWriter,
        SearchProgressState progress,
        CancellationToken cancellationToken)
    {
        var pendingDirectories = new Stack<string>();
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matcher = new FarMaskMatcher();
        IReadOnlyDictionary<string, MaskGroup> groups = FarDefaultHighlightPreset.GroupsByName;

        pendingDirectories.Push(request.RootPath);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directoryPath = pendingDirectories.Pop();
            string directoryIdentity = GetDirectoryIdentity(directoryPath, progress);
            if (!visitedDirectories.Add(directoryIdentity))
                continue;

            progress.ReportDirectory(directoryPath);

            var directoryInfo = new DirectoryInfo(directoryPath);
            var directories = EnumerateDirectories(directoryInfo, progress);
            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = SafeGetAttributes(directory, FileAttributes.Directory, progress);

                if (request.IncludeDirectoriesInResults &&
                    matcher.IsMatch(request.FileMaskExpression, directory.Name, groups, request.CaseSensitive))
                {
                    progress.ReportMatched(directory.FullName);
                    await resultWriter.WriteAsync(
                        CreateDirectoryResult(directory, attributes),
                        cancellationToken).ConfigureAwait(false);
                }

                if (request.Scope == SearchScope.CurrentDirectoryOnly)
                    continue;

                bool isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
                if (isReparsePoint && !request.SearchInSymbolicLinks)
                    continue;

                string? childIdentity = TryGetChildDirectoryIdentity(directory, attributes, progress);
                if (childIdentity is null || visitedDirectories.Contains(childIdentity))
                    continue;

                pendingDirectories.Push(directory.FullName);
            }

            var files = EnumerateFiles(directoryInfo, progress);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.ReportFile(file.FullName);

                if (!matcher.IsMatch(request.FileMaskExpression, file.Name, groups, request.CaseSensitive))
                    continue;

                var candidate = CreateFileCandidate(file, progress);
                if (candidate is null)
                    continue;

                if (request.ContainingText is null)
                {
                    progress.ReportMatched(candidate.FullPath);
                    await resultWriter.WriteAsync(candidate.ToResult(), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await fileWriter.WriteAsync(candidate, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task ProcessFileCandidatesAsync(
        SearchRequest request,
        ChannelReader<FileSearchCandidate> fileReader,
        ChannelWriter<SearchResultItem> resultWriter,
        SearchProgressState progress,
        CancellationToken cancellationToken)
    {
        await foreach (var candidate in fileReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await TryMatchFileContentAsync(request, candidate, progress, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
                continue;

            progress.ReportMatched(candidate.FullPath);
            await resultWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<SearchResultItem?> TryMatchFileContentAsync(
        SearchRequest request,
        FileSearchCandidate candidate,
        SearchProgressState progress,
        CancellationToken cancellationToken)
    {
        if (candidate.Size > request.MaxContentSearchFileSizeBytes)
        {
            progress.ReportError(candidate.FullPath, "Skipped file larger than the content search limit.");
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = await ReadFileBytesAsync(
                candidate.FullPath,
                request.MaxContentSearchFileSizeBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
            progress.ReportError(candidate.FullPath, ex.Message);
            return null;
        }

        if (LooksBinary(bytes))
        {
            progress.ReportError(candidate.FullPath, "Skipped binary file.");
            return null;
        }

        string? text = DecodeText(bytes, candidate.FullPath, progress);
        if (text is null)
            return null;

        var comparison = request.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var match = FindContentMatch(text, request.ContainingText!, comparison, request.WholeWords);

        if (request.NotContaining)
            return match is null ? candidate.ToResult() : null;

        return match is null
            ? null
            : candidate.ToResult(match.Preview, match.LineNumber);
    }

    private static async Task<byte[]> ReadFileBytesAsync(
        string fullPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileReadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length > maxBytes)
            throw new IOException("File grew beyond the content search limit.");

        using var memory = new MemoryStream(stream.Length > int.MaxValue ? 0 : (int)stream.Length);
        var buffer = new byte[FileReadBufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            memory.Write(buffer, 0, read);
            if (memory.Length > maxBytes)
                throw new IOException("File grew beyond the content search limit.");
        }

        return memory.ToArray();
    }

    private static string? DecodeText(byte[] bytes, string fullPath, SearchProgressState progress)
    {
        try
        {
            var (encoding, preambleLength) = DetectEncoding(bytes);
            return encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException)
        {
            progress.ReportError(fullPath, ex.Message);
            return null;
        }
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (StartsWith(bytes, [0xEF, 0xBB, 0xBF]))
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true), 3);

        if (StartsWith(bytes, [0xFF, 0xFE]))
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true), 2);

        if (StartsWith(bytes, [0xFE, 0xFF]))
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true), 2);

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            _ = utf8.GetString(bytes);
            return (utf8, 0);
        }
        catch (DecoderFallbackException)
        {
            int ansiCodePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            return (Encoding.GetEncoding(
                ansiCodePage,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback), 0);
        }
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
            return false;

        for (int i = 0; i < prefix.Length; i++)
            if (bytes[i] != prefix[i])
                return false;

        return true;
    }

    private static bool LooksBinary(byte[] bytes)
    {
        if (bytes.Length == 0 ||
            StartsWith(bytes, [0xEF, 0xBB, 0xBF]) ||
            StartsWith(bytes, [0xFF, 0xFE]) ||
            StartsWith(bytes, [0xFE, 0xFF]))
        {
            return false;
        }

        int sampleLength = Math.Min(bytes.Length, BinarySampleSize);
        int nulCount = 0;
        for (int i = 0; i < sampleLength; i++)
        {
            if (bytes[i] == 0)
                nulCount++;
        }

        return nulCount > 0;
    }

    private static ContentMatch? FindContentMatch(
        string text,
        string needle,
        StringComparison comparison,
        bool wholeWords)
    {
        int index = 0;
        while (index <= text.Length)
        {
            int found = text.IndexOf(needle, index, comparison);
            if (found < 0)
                return null;

            if (!wholeWords || IsWholeWordMatch(text, found, needle.Length))
                return CreateContentMatch(text, found);

            index = found + Math.Max(1, needle.Length);
        }

        return null;
    }

    private static bool IsWholeWordMatch(string text, int index, int length)
    {
        bool leftOk = index == 0 || !IsWordCharacter(text[index - 1]);
        int rightIndex = index + length;
        bool rightOk = rightIndex >= text.Length || !IsWordCharacter(text[rightIndex]);
        return leftOk && rightOk;
    }

    private static bool IsWordCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private static ContentMatch CreateContentMatch(string text, int index)
    {
        int lineNumber = 1;
        for (int i = 0; i < index; i++)
            if (text[i] == '\n')
                lineNumber++;

        int lineStart = text.LastIndexOf('\n', Math.Max(0, index));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        int lineEnd = text.IndexOf('\n', index);
        if (lineEnd < 0)
            lineEnd = text.Length;

        string preview = text[lineStart..lineEnd].TrimEnd('\r');
        if (preview.Length > PreviewMaxLength)
            preview = preview[..PreviewMaxLength];

        return new ContentMatch(lineNumber, preview);
    }

    private static IReadOnlyList<DirectoryInfo> EnumerateDirectories(
        DirectoryInfo directory,
        SearchProgressState progress)
    {
        try
        {
            return directory.EnumerateDirectories().ToArray();
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
            progress.ReportError(directory.FullName, ex.Message);
            return [];
        }
    }

    private static IReadOnlyList<FileInfo> EnumerateFiles(
        DirectoryInfo directory,
        SearchProgressState progress)
    {
        try
        {
            return directory.EnumerateFiles().ToArray();
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
            progress.ReportError(directory.FullName, ex.Message);
            return [];
        }
    }

    private static FileAttributes SafeGetAttributes(
        FileSystemInfo info,
        FileAttributes fallback,
        SearchProgressState progress)
    {
        try
        {
            return info.Attributes;
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
            progress.ReportError(info.FullName, ex.Message);
            return fallback;
        }
    }

    private static FileSearchCandidate? CreateFileCandidate(FileInfo file, SearchProgressState progress)
    {
        try
        {
            return new FileSearchCandidate(
                file.FullName,
                file.Name,
                file.Length,
                file.LastWriteTime,
                file.Attributes);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
            progress.ReportError(file.FullName, ex.Message);
            return null;
        }
    }

    private static SearchResultItem CreateDirectoryResult(DirectoryInfo directory, FileAttributes attributes)
    {
        DateTime lastWriteTime;
        try { lastWriteTime = directory.LastWriteTime; }
        catch { lastWriteTime = default; }

        return new SearchResultItem
        {
            FullPath = directory.FullName,
            Name = directory.Name,
            Kind = SearchResultItemKind.Directory,
            LastWriteTime = lastWriteTime,
            Attributes = attributes | FileAttributes.Directory,
        };
    }

    private static string GetDirectoryIdentity(string directoryPath, SearchProgressState progress)
    {
        try
        {
            return NormalizeDirectoryIdentity(new DirectoryInfo(directoryPath).FullName);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex) || ex is ArgumentException or NotSupportedException)
        {
            progress.ReportError(directoryPath, ex.Message);
            return NormalizeDirectoryIdentity(directoryPath);
        }
    }

    private static string? TryGetChildDirectoryIdentity(
        DirectoryInfo directory,
        FileAttributes attributes,
        SearchProgressState progress)
    {
        try
        {
            if ((attributes & FileAttributes.ReparsePoint) == 0)
                return NormalizeDirectoryIdentity(directory.FullName);

            var target = directory.ResolveLinkTarget(returnFinalTarget: true);
            return target is null
                ? NormalizeDirectoryIdentity(directory.FullName)
                : NormalizeDirectoryIdentity(target.FullName);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex) || ex is ArgumentException or NotSupportedException)
        {
            progress.ReportError(directory.FullName, ex.Message);
            return null;
        }
    }

    private static string NormalizeDirectoryIdentity(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsRecoverableFileSystemException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or FileNotFoundException or PathTooLongException;

    private sealed record FileSearchCandidate(
        string FullPath,
        string Name,
        long Size,
        DateTime LastWriteTime,
        FileAttributes Attributes)
    {
        public SearchResultItem ToResult(string? preview = null, int? lineNumber = null) =>
            new()
            {
                FullPath = FullPath,
                Name = Name,
                Kind = SearchResultItemKind.File,
                Size = Size,
                LastWriteTime = LastWriteTime,
                Attributes = Attributes,
                MatchedTextPreview = preview,
                LineNumber = lineNumber,
            };
    }

    private sealed record ContentMatch(int LineNumber, string Preview);

    private sealed class SearchProgressState
    {
        private readonly IProgress<SearchProgress>? _progress;
        private readonly object _gate = new();
        private long _scannedDirectories;
        private long _scannedFiles;
        private long _matchedItems;
        private long _errorCount;
        private string? _currentPath;
        private string? _lastErrorPath;
        private string? _lastErrorMessage;

        public SearchProgressState(IProgress<SearchProgress>? progress)
        {
            _progress = progress;
        }

        public void ReportDirectory(string currentPath)
        {
            Interlocked.Increment(ref _scannedDirectories);
            SetCurrentPath(currentPath);
            Report();
        }

        public void ReportFile(string currentPath)
        {
            Interlocked.Increment(ref _scannedFiles);
            SetCurrentPath(currentPath);
            Report();
        }

        public void ReportMatched(string currentPath)
        {
            Interlocked.Increment(ref _matchedItems);
            SetCurrentPath(currentPath);
            Report();
        }

        public void ReportError(string path, string message)
        {
            Interlocked.Increment(ref _errorCount);
            lock (_gate)
            {
                _currentPath = path;
                _lastErrorPath = path;
                _lastErrorMessage = message;
            }
            Report();
        }

        private void SetCurrentPath(string currentPath)
        {
            lock (_gate)
                _currentPath = currentPath;
        }

        private void Report()
        {
            if (_progress is null)
                return;

            string? currentPath;
            string? lastErrorPath;
            string? lastErrorMessage;
            lock (_gate)
            {
                currentPath = _currentPath;
                lastErrorPath = _lastErrorPath;
                lastErrorMessage = _lastErrorMessage;
            }

            _progress.Report(new SearchProgress
            {
                ScannedDirectories = Interlocked.Read(ref _scannedDirectories),
                ScannedFiles = Interlocked.Read(ref _scannedFiles),
                MatchedItems = Interlocked.Read(ref _matchedItems),
                ErrorCount = Interlocked.Read(ref _errorCount),
                CurrentPath = currentPath,
                LastErrorPath = lastErrorPath,
                LastErrorMessage = lastErrorMessage,
            });
        }
    }
}
