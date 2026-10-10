using System.Text;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi;

[Flags]
internal enum KittyKeyboardFlags
{
    None = 0,
    DisambiguateEscapeCodes = 1,
    ReportEventTypes = 2,
    ReportAlternateKeys = 4,
    ReportAllKeysAsEscapeCodes = 8,
    ReportAssociatedText = 16,
}

internal enum TerminalKeyboardScreen
{
    Main,
    Alternate,
}

internal sealed class ReplayAnsiInputByteReader : IAnsiInputByteReader
{
    private readonly IAnsiInputByteReader _inner;
    private readonly Queue<byte> _replay = new();

    public ReplayAnsiInputByteReader(IAnsiInputByteReader inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public byte ReadByte() =>
        _replay.Count > 0 ? _replay.Dequeue() : _inner.ReadByte();

    public bool TryReadByte(out byte value)
    {
        if (_replay.Count > 0)
        {
            value = _replay.Dequeue();
            return true;
        }

        return _inner.TryReadByte(out value);
    }

    public bool WaitForInput(int timeoutMilliseconds) =>
        _replay.Count > 0 || _inner.WaitForInput(timeoutMilliseconds);

    public void Replay(IEnumerable<byte> bytes)
    {
        foreach (byte value in bytes)
            _replay.Enqueue(value);
    }
}

internal sealed class KittyKeyboardProtocolController : IDisposable
{
    private const string Csi = "\x1b[";
    private const int DefaultResponseTimeoutMilliseconds = 200;
    private const KittyKeyboardFlags BasicFlags =
        KittyKeyboardFlags.DisambiguateEscapeCodes |
        KittyKeyboardFlags.ReportAllKeysAsEscapeCodes |
        KittyKeyboardFlags.ReportAssociatedText;
    private const KittyKeyboardFlags ProductionFlags = BasicFlags | KittyKeyboardFlags.ReportEventTypes;

    private readonly ReplayAnsiInputByteReader _input;
    private readonly Action<string> _writeControl;
    private readonly int _responseTimeoutMilliseconds;
    private readonly bool _requested;
    private readonly AnsiInputParser _responseParser = new(50);
    private bool _detectionAttempted;
    private string _supportStatus;
    private int? _confirmedFlags;
    private bool _inputActive;
    private bool _pushed;
    private KittyKeyboardFlags _activeProfile;
    private bool _disposed;
    private TerminalKeyboardScreen _screen = TerminalKeyboardScreen.Main;
    private string? _fallbackReason;

    public KittyKeyboardProtocolController(
        ReplayAnsiInputByteReader input,
        Action<string> writeControl,
        bool requested = true,
        int responseTimeoutMilliseconds = DefaultResponseTimeoutMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(responseTimeoutMilliseconds);
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _writeControl = writeControl ?? throw new ArgumentNullException(nameof(writeControl));
        _requested = requested;
        _responseTimeoutMilliseconds = responseTimeoutMilliseconds;
        _supportStatus = requested ? "unknown" : "not requested";
        if (!requested)
            _fallbackReason = "enhanced keyboard not requested";
    }

    public KeyboardProtocolSnapshot Snapshot => new(
        protocol: _activeProfile != KittyKeyboardFlags.None ? "kitty" : "legacy-vt",
        supportStatus: _supportStatus,
        requested: _requested,
        requestedFlags: _requested ? (int)ProductionFlags : 0,
        confirmedFlags: _confirmedFlags,
        isActive: _activeProfile != KittyKeyboardFlags.None,
        activeScreen: _activeProfile != KittyKeyboardFlags.None ? ScreenName(_screen) : null,
        fallbackReason: _fallbackReason,
        activeFlags: (int)_activeProfile);

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _inputActive = true;
        if (!_requested)
            return;

        if (!_detectionAttempted)
            DetectSupport();

        if (_supportStatus == "supported")
            ActivateCurrentScreen();
    }

    public void Suspend()
    {
        if (_disposed)
            return;

        DeactivateCurrentScreen();
        _inputActive = false;
    }

    public void PrepareForScreenChange()
    {
        if (_disposed)
            return;

        DeactivateCurrentScreen();
    }

    public void CompleteScreenChange(bool alternate)
    {
        if (_disposed)
            return;

        _screen = alternate ? TerminalKeyboardScreen.Alternate : TerminalKeyboardScreen.Main;
        if (_inputActive && _supportStatus == "supported")
            ActivateCurrentScreen();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DeactivateCurrentScreen();
        _inputActive = false;
        _disposed = true;
    }

    private void DetectSupport()
    {
        _detectionAttempted = true;
        _confirmedFlags = null;
        _fallbackReason = null;

        try
        {
            _writeControl($"{Csi}?u{Csi}c");

            bool sawPrimaryDeviceAttributes = false;
            int? flags = null;
            var unrelated = new List<byte>();
            long deadline = Environment.TickCount64 + _responseTimeoutMilliseconds;

            while (TryReadResponseBefore(deadline, out byte[] bytes))
            {
                if (TryParseKeyboardFlags(bytes, out int parsedFlags))
                {
                    flags = parsedFlags;
                    continue;
                }

                if (IsPrimaryDeviceAttributes(bytes))
                {
                    sawPrimaryDeviceAttributes = true;
                    if (flags.HasValue)
                        break;
                    continue;
                }

                unrelated.AddRange(bytes);
            }

            _input.Replay(unrelated);
            _confirmedFlags = flags;

            if (flags.HasValue)
            {
                _supportStatus = "supported";
                return;
            }

            if (sawPrimaryDeviceAttributes)
            {
                _supportStatus = "unsupported";
                _fallbackReason = "terminal did not report Kitty keyboard flags";
                return;
            }

            _supportStatus = "unknown";
            _fallbackReason = "negotiation timeout";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _supportStatus = "unknown";
            _confirmedFlags = null;
            _fallbackReason = $"negotiation failed: {ex.Message}";
        }
    }

    private void ActivateCurrentScreen()
    {
        if (_pushed || !_inputActive)
            return;

        if (TryActivateProfile(ProductionFlags, out string fullReason))
        {
            _fallbackReason = null;
            return;
        }

        _fallbackReason = fullReason;
        // A failed pop leaves ownership of the terminal stack uncertain.
        // Never push another level until our previous level was removed.
        if (_pushed)
            return;

        if (TryActivateProfile(BasicFlags, out string basicReason))
        {
            _fallbackReason = fullReason;
            return;
        }

        _fallbackReason = $"{fullReason}; basic Kitty fallback failed: {basicReason}";
    }

    private bool TryActivateProfile(KittyKeyboardFlags flags, out string reason)
    {
        reason = "";
        try
        {
            _writeControl($"{Csi}>{(int)flags}u");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // The writer did not report success. We cannot infer that a stack
            // entry exists, so issuing a blind pop would corrupt another owner.
            reason = $"push {(int)flags} failed: {ex.Message}";
            return false;
        }

        _pushed = true;
        int? confirmed = null;
        try
        {
            confirmed = QueryCurrentFlags();
            _confirmedFlags = confirmed;
            if (confirmed.HasValue && (confirmed.Value & (int)flags) == (int)flags)
            {
                _activeProfile = flags;
                return true;
            }

            reason = confirmed.HasValue
                ? $"requested flags {(int)flags} were not confirmed (actual {confirmed.Value})"
                : $"activation confirmation timeout for flags {(int)flags}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            reason = $"confirmation of flags {(int)flags} failed: {ex.Message}";
        }

        if (!TryPopCurrentScreen())
            reason += "; failed to restore previous keyboard mode";
        return false;
    }

    private int? QueryCurrentFlags()
    {
        _writeControl($"{Csi}?u");
        var unrelated = new List<byte>();
        long deadline = Environment.TickCount64 + _responseTimeoutMilliseconds;

        while (TryReadResponseBefore(deadline, out byte[] bytes))
        {
            if (TryParseKeyboardFlags(bytes, out int flags))
            {
                _input.Replay(unrelated);
                return flags;
            }

            unrelated.AddRange(bytes);
        }

        _input.Replay(unrelated);
        return null;
    }

    private void DeactivateCurrentScreen()
    {
        if (!TryPopCurrentScreen())
            _fallbackReason = "failed to restore previous keyboard mode";
    }

    private bool TryPopCurrentScreen()
    {
        if (!_pushed)
            return true;

        try
        {
            _writeControl($"{Csi}<u");
            _pushed = false;
            _activeProfile = KittyKeyboardFlags.None;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _fallbackReason = $"failed to pop keyboard mode: {ex.Message}";
            return false;
        }
    }

    private bool TryReadResponseBefore(long deadline, out byte[] bytes)
    {
        int remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
        if (remaining <= 0 || !_input.WaitForInput(remaining))
        {
            bytes = [];
            return false;
        }

        try
        {
            bytes = _responseParser.Read(_input).Bytes;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or DecoderFallbackException)
        {
            bytes = [];
            return false;
        }
    }

    private static bool TryParseKeyboardFlags(ReadOnlySpan<byte> bytes, out int flags)
    {
        flags = 0;
        if (bytes.Length < 5 ||
            bytes[0] != 0x1b ||
            bytes[1] != (byte)'[' ||
            bytes[2] != (byte)'?' ||
            bytes[^1] != (byte)'u')
        {
            return false;
        }

        return int.TryParse(Encoding.ASCII.GetString(bytes[3..^1]), out flags) && flags >= 0;
    }

    private static bool IsPrimaryDeviceAttributes(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 &&
        bytes[0] == 0x1b &&
        bytes[1] == (byte)'[' &&
        bytes[2] == (byte)'?' &&
        bytes[^1] == (byte)'c';

    private static string ScreenName(TerminalKeyboardScreen screen) =>
        screen == TerminalKeyboardScreen.Alternate ? "alternate" : "main";
}
