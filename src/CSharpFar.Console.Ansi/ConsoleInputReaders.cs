using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Console.Ansi;

internal abstract class ConsoleInputReaderBase : IConsoleInputReader
{
    private readonly Func<ConsoleSize> _getSize;
    private readonly Action _resetCachedOutputState;
    private ConsoleSize _lastKnownSize;

    protected ConsoleInputReaderBase(Func<ConsoleSize> getSize, Action resetCachedOutputState)
    {
        _getSize = getSize;
        _resetCachedOutputState = resetCachedOutputState;
        _lastKnownSize = getSize();
    }

    public abstract string BackendName { get; }

    public abstract bool MouseTrackingEnabled { get; }

    public abstract ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default);

    public abstract bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent);

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        while (true)
        {
            if (ReadInput(intercept) is KeyConsoleInputEvent keyEvent)
                return keyEvent.Key;
        }
    }

    public abstract void SuspendInputMode();

    public abstract void RestoreInputMode();

    public abstract void Dispose();

    protected bool TryReadResize([NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        var size = _getSize();
        if (size.Width == _lastKnownSize.Width && size.Height == _lastKnownSize.Height)
        {
            inputEvent = null;
            return false;
        }

        _lastKnownSize = size;
        _resetCachedOutputState();
        inputEvent = new ConsoleResizeInputEvent();
        return true;
    }
}

internal sealed class UnixRawTerminalInputReader : ConsoleInputReaderBase
{
    private const string EnableMouseTracking = "\x1b[?1002h\x1b[?1006h";
    private const string DisableMouseTracking = "\x1b[?1000l\x1b[?1002l\x1b[?1003l\x1b[?1006l";
    private const int CancellationPollMilliseconds = 50;

    private readonly IAnsiInputByteReader _input;
    private readonly AnsiConsoleInputParser _parser = new();
    private readonly ITerminalInputMode _terminalMode;
    private readonly Action<string> _writeControl;
    private bool _active;
    private bool _disposed;

    public UnixRawTerminalInputReader(
        IAnsiInputByteReader input,
        Func<ConsoleSize> getSize,
        Action resetCachedOutputState,
        Action<string> writeControl,
        ITerminalInputMode? terminalMode = null)
        : base(getSize, resetCachedOutputState)
    {
        _input = input;
        _writeControl = writeControl;
        _terminalMode = terminalMode ?? new UnixTerminalMode();
        try
        {
            EnableInputMode();
        }
        catch
        {
            _terminalMode.Dispose();
            throw;
        }
    }

    public override string BackendName => "raw-vt";

    public override bool MouseTrackingEnabled => _active;

    public override ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadResize(out var resize))
                return resize;

            if (!_input.WaitForInput(CancellationPollMilliseconds))
                continue;

            if (TryReadParsedEvent(intercept, out var inputEvent))
                return inputEvent;
        }
    }

    public override bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (TryReadResize(out inputEvent))
            return true;

        while (_input.WaitForInput(0))
        {
            if (TryReadParsedEvent(intercept, out inputEvent))
                return true;
        }

        inputEvent = null;
        return false;
    }

    public override void SuspendInputMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_active)
            return;

        try
        {
            _writeControl(DisableMouseTracking);
        }
        finally
        {
            _terminalMode.RestoreOriginalMode();
            _active = false;
        }
    }

    public override void RestoreInputMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnableInputMode();
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_active)
                _writeControl(DisableMouseTracking);
        }
        finally
        {
            _active = false;
            _terminalMode.Dispose();
            _disposed = true;
        }
    }

    private bool TryReadParsedEvent(
        bool intercept,
        [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        if (!_parser.TryRead(_input, out inputEvent))
            return false;

        if (!intercept && inputEvent is KeyConsoleInputEvent { Key.KeyChar: not '\0' } keyEvent)
            _writeControl(keyEvent.Key.KeyChar.ToString());

        return true;
    }

    private void EnableInputMode()
    {
        if (_active)
            return;

        _terminalMode.EnableRawMode();
        try
        {
            _writeControl(EnableMouseTracking);
            _active = true;
        }
        catch
        {
            _terminalMode.RestoreOriginalMode();
            throw;
        }
    }

}
