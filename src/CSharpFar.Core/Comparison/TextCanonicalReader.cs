using System.Text;
using CSharpFar.Core.Text;

namespace CSharpFar.Core.Comparison;

internal sealed class TextCanonicalReader : IDisposable
{
    internal const int DefaultByteBufferSize = 16 * 1024;
    internal const int DefaultDetectionPrefixSize = 8 * 1024;

    private readonly Stream _stream;
    private readonly ComparisonOptions _options;
    private readonly byte[] _prefix;
    private readonly int _prefixCount;
    private readonly byte[] _byteBuffer;
    private readonly char[] _decodedBuffer;
    private readonly char[] _canonicalBuffer;
    private readonly Decoder _decoder;

    private int _prefixOffset;
    private int _canonicalOffset;
    private int _canonicalCount;
    private bool _decoderFinished;
    private bool _pendingCarriageReturn;
    private bool _inWhitespaceRun;
    private bool _disposed;

    public TextCanonicalReader(
        Stream stream,
        ComparisonOptions options,
        CancellationToken cancellationToken = default,
        int byteBufferSize = DefaultByteBufferSize,
        int detectionPrefixSize = DefaultDetectionPrefixSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        if (byteBufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteBufferSize));
        if (detectionPrefixSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(detectionPrefixSize));

        _stream = stream;
        _options = options;
        _prefix = new byte[checked(detectionPrefixSize + 3)];
        _prefixCount = ReadPrefix(_stream, _prefix, cancellationToken);
        SourceBytesRead = _prefixCount;

        Detection = DetectPrefix(_prefix.AsSpan(0, _prefixCount), detectionPrefixSize);
        _prefixOffset = Math.Min(Detection.ContentStartLength, _prefixCount);

        _byteBuffer = new byte[byteBufferSize];
        _decoder = Detection.Encoding.GetDecoder();
        _decodedBuffer = new char[Detection.Encoding.GetMaxCharCount(byteBufferSize)];
        _canonicalBuffer = new char[_decodedBuffer.Length + 2];
    }

    public EncodingDetectionResult Detection { get; }

    public long SourceBytesRead { get; private set; }

    public int Read(Span<char> destination, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Detection.IsBinary)
            throw new InvalidOperationException("Binary content cannot be read as canonical text.");

        int written = 0;
        while (written < destination.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_canonicalOffset < _canonicalCount)
            {
                int copy = Math.Min(destination.Length - written, _canonicalCount - _canonicalOffset);
                _canonicalBuffer.AsSpan(_canonicalOffset, copy).CopyTo(destination[written..]);
                _canonicalOffset += copy;
                written += copy;
                continue;
            }

            if (_decoderFinished)
                break;

            FillCanonicalBuffer(cancellationToken);
        }

        return written;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stream.Dispose();
    }

    private void FillCanonicalBuffer(CancellationToken cancellationToken)
    {
        _canonicalOffset = 0;
        _canonicalCount = 0;

        while (_canonicalCount == 0 && !_decoderFinished)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = ReadSourceBytes(cancellationToken);
            if (read > 0)
            {
                int chars = _decoder.GetChars(_byteBuffer, 0, read, _decodedBuffer, 0, flush: false);
                Normalize(_decodedBuffer.AsSpan(0, chars));
                continue;
            }

            int flushedChars = _decoder.GetChars(
                Array.Empty<byte>(), 0, 0, _decodedBuffer, 0, flush: true);
            Normalize(_decodedBuffer.AsSpan(0, flushedChars));
            FlushNormalization();
            _decoderFinished = true;
        }
    }

    private int ReadSourceBytes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_prefixOffset < _prefixCount)
        {
            int count = Math.Min(_byteBuffer.Length, _prefixCount - _prefixOffset);
            _prefix.AsSpan(_prefixOffset, count).CopyTo(_byteBuffer);
            _prefixOffset += count;
            return count;
        }

        int read = _stream.Read(_byteBuffer, 0, _byteBuffer.Length);
        SourceBytesRead += read;
        cancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    private void Normalize(ReadOnlySpan<char> chars)
    {
        foreach (char value in chars)
        {
            if (_options.TextLineEndings == TextLineEndingComparison.Normalize)
            {
                if (_pendingCarriageReturn)
                {
                    if (value == '\n')
                    {
                        EmitAfterLineEndingNormalization('\n');
                        _pendingCarriageReturn = false;
                        continue;
                    }

                    EmitAfterLineEndingNormalization('\n');
                    _pendingCarriageReturn = false;
                }

                if (value == '\r')
                {
                    _pendingCarriageReturn = true;
                    continue;
                }

                if (value == '\n')
                {
                    EmitAfterLineEndingNormalization('\n');
                    continue;
                }
            }
            else if (_options.TextLineEndings != TextLineEndingComparison.Exact)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(_options.TextLineEndings),
                    _options.TextLineEndings,
                    "Unsupported line-ending comparison mode.");
            }

            EmitAfterLineEndingNormalization(value);
        }
    }

    private void FlushNormalization()
    {
        if (_pendingCarriageReturn)
        {
            EmitAfterLineEndingNormalization('\n');
            _pendingCarriageReturn = false;
        }
    }

    private void EmitAfterLineEndingNormalization(char value)
    {
        bool horizontalWhitespace = value is ' ' or '\t';
        if (!horizontalWhitespace)
        {
            _inWhitespaceRun = false;
            Append(value);
            return;
        }

        switch (_options.TextWhitespace)
        {
            case TextWhitespaceMode.Exact:
                Append(value);
                break;
            case TextWhitespaceMode.Normalize:
                if (!_inWhitespaceRun)
                    Append(' ');
                _inWhitespaceRun = true;
                break;
            case TextWhitespaceMode.IgnoreAll:
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(_options.TextWhitespace),
                    _options.TextWhitespace,
                    "Unsupported whitespace comparison mode.");
        }
    }

    private void Append(char value)
    {
        if (_canonicalCount >= _canonicalBuffer.Length)
            throw new InvalidOperationException("Canonical text buffer overflow.");
        _canonicalBuffer[_canonicalCount++] = value;
    }

    private static EncodingDetectionResult DetectPrefix(
        ReadOnlySpan<byte> prefix,
        int detectionPrefixSize)
    {
        int initialLength = Math.Min(prefix.Length, detectionPrefixSize);
        EncodingDetectionResult initial = TextEncodingDetector.Detect(prefix[..initialLength]);

        if (prefix.Length <= initialLength ||
            initial.IsBinary ||
            initial.IsUtf16 ||
            initial.HasByteOrderMark ||
            initial.Encoding.CodePage == Encoding.UTF8.CodePage)
        {
            return initial;
        }

        int maxLength = Math.Min(prefix.Length, initialLength + 3);
        for (int length = initialLength + 1; length <= maxLength; length++)
        {
            EncodingDetectionResult candidate = TextEncodingDetector.Detect(prefix[..length]);
            if (!candidate.IsBinary && candidate.Encoding.CodePage == Encoding.UTF8.CodePage)
                return candidate;
        }

        return initial;
    }

    private static int ReadPrefix(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}
