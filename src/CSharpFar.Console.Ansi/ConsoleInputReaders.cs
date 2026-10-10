using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Console.Ansi;

internal interface IMouseTrackingControl
{
    void SetMouseTrackingEnabled(bool enabled);
}

internal interface IKeyboardProtocolControl
{
    KeyboardProtocolSnapshot KeyboardProtocol { get; }

    void PrepareForScreenChange();

    void CompleteScreenChange(bool alternate);
}

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

    public string InputBackendName => BackendName;

    public abstract bool MouseTrackingEnabled { get; }

    public virtual KeyboardProtocolSnapshot KeyboardProtocol =>
        KeyboardProtocolSnapshot.NotApplicable;

    public virtual ModifierKeyTrackingSnapshot ModifierKeyTracking =>
        ModifierKeyTrackerFactory.UnsupportedSnapshot;

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

internal sealed class UnixRawTerminalInputReader : ConsoleInputReaderBase, IMouseTrackingControl, IKeyboardProtocolControl
{
    private const string EnableMouseTracking = "\x1b[?1003h\x1b[?1006h";
    private const string DisableMouseTracking = "\x1b[?1000l\x1b[?1002l\x1b[?1003l\x1b[?1006l";
    private const int CancellationPollMilliseconds = 50;
    private const string EnableFocusReporting = "\x1b[?1004h";
    private const string DisableFocusReporting = "\x1b[?1004l";

    private readonly IAnsiInputByteReader _input;
    private readonly KittyKeyboardProtocolController _keyboardProtocol;
    private readonly AnsiConsoleInputParser _parser;
    private readonly ITerminalInputMode _terminalMode;
    private readonly IModifierKeyTracker? _modifierKeyTracker;
    private readonly Action<string> _writeControl;
    private bool _active;
    private bool _mouseTrackingRequested = true;
    private bool _mouseTrackingActive;
    private bool _focusReportingActive;
    private bool _hasFocus = true;
    private bool _pendingModifierReset;
    private ConsoleModifiers _heldModifiers;
    private bool _disposed;

    public UnixRawTerminalInputReader(
        IAnsiInputByteReader input,
        Func<ConsoleSize> getSize,
        Action resetCachedOutputState,
        Action<string> writeControl,
        ITerminalInputMode? terminalMode = null,
        IModifierKeyTracker? modifierKeyTracker = null,
        AnsiConsoleInputParser? parser = null,
        bool enhancedKeyboardRequested = false)
        : base(getSize, resetCachedOutputState)
    {
        var replayInput = new ReplayAnsiInputByteReader(input);
        _input = replayInput;
        _writeControl = writeControl;
        _keyboardProtocol = new KittyKeyboardProtocolController(
            replayInput,
            writeControl,
            enhancedKeyboardRequested);
        _parser = parser ?? new AnsiConsoleInputParser();
        _terminalMode = terminalMode ?? new LinuxTerminalInputMode();
        _modifierKeyTracker = modifierKeyTracker ?? ModifierKeyTrackerFactory.TryCreateForCurrentPlatform();
        try
        {
            EnableInputMode();
        }
        catch
        {
            _terminalMode.Dispose();
            _modifierKeyTracker?.Dispose();
            throw;
        }
    }

    public override string BackendName => "raw-vt";

    public override bool MouseTrackingEnabled => _mouseTrackingActive;

    public override KeyboardProtocolSnapshot KeyboardProtocol => _keyboardProtocol.Snapshot;

    public override ModifierKeyTrackingSnapshot ModifierKeyTracking
    {
        get
        {
            ModifierKeyTrackingSnapshot native =
                _modifierKeyTracker?.GetSnapshot() ?? ModifierKeyTrackerFactory.UnsupportedSnapshot;
            if (KittyTracksModifiers)
            {
                return native with
                {
                    ActiveSource = "kitty",
                    SupportedModifiers = "Shift, Alt, Control",
                    ProtocolTrackingAvailable = true,
                };
            }

            return native.IsEnabled && native.CanTrackShiftOnly
                ? native with { ActiveSource = "linux-evdev", SupportedModifiers = "Shift" }
                : native;
        }
    }

    private bool KittyTracksModifiers => _keyboardProtocol.Snapshot.CanTrackStandaloneModifiers;

    public override ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadModifierStateEvent(out var modifierEvent))
                return modifierEvent;

            if (TryReadResize(out var resize))
                return resize;

            if (!_input.WaitForInput(CancellationPollMilliseconds))
            {
                if (TryReadModifierStateEvent(out modifierEvent))
                    return modifierEvent;
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (TryReadParsedEvent(intercept, out var inputEvent))
                return inputEvent;
        }
    }

    public override bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (TryReadModifierStateEvent(out var modifierEvent))
        {
            inputEvent = modifierEvent;
            return true;
        }

        if (TryReadResize(out inputEvent))
            return true;

        while (_input.WaitForInput(0))
        {
            if (TryReadParsedEvent(intercept, out inputEvent))
                return true;
            if (TryReadModifierStateEvent(out var pending))
            {
                inputEvent = pending;
                return true;
            }
        }

        inputEvent = null;
        return false;
    }

    public void PrepareForScreenChange()
    {
        ResetHeldModifiers();
        _modifierKeyTracker?.Suspend();
        _keyboardProtocol.PrepareForScreenChange();
    }

    public void CompleteScreenChange(bool alternate)
    {
        _keyboardProtocol.CompleteScreenChange(alternate);
        if (_active && !KittyTracksModifiers)
            _modifierKeyTracker?.Resume();
    }

    public void SetMouseTrackingEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _mouseTrackingRequested = enabled;
        if (!_active || _mouseTrackingActive == enabled)
            return;

        ApplyMouseTracking(enabled);
    }

    public override void SuspendInputMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_active)
            return;

        try
        {
            ResetHeldModifiers();
            _keyboardProtocol.Suspend();
            DisableFocus();
            _parser.ResetMouseState();
            _modifierKeyTracker?.Suspend();
            if (_mouseTrackingActive)
                _writeControl(DisableMouseTracking);
        }
        finally
        {
            _mouseTrackingActive = false;
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
            _keyboardProtocol.Dispose();
            DisableFocus();
            _writeControl(DisableMouseTracking);
        }
        finally
        {
            _mouseTrackingActive = false;
            _active = false;
            _terminalMode.Dispose();
            _modifierKeyTracker?.Dispose();
            _disposed = true;
        }
    }

    private bool TryReadModifierStateEvent(
        [NotNullWhen(true)] out ModifierKeyConsoleInputEvent? inputEvent)
    {
        if (_pendingModifierReset)
        {
            _pendingModifierReset = false;
            inputEvent = new ModifierKeyConsoleInputEvent(default);
            return true;
        }

        inputEvent = null;
        if (!_hasFocus || KittyTracksModifiers || _modifierKeyTracker is null)
            return false;

        if (!_modifierKeyTracker.TryCreateInputEvent(out var native) || native is null)
            return false;

        if (!TryPublishModifiers(native.Modifiers, out _))
            return false;

        // Preserve the native tracker event identity for existing consumers.
        inputEvent = native;
        return true;
    }

    private bool TryPublishModifiers(
        ConsoleModifiers modifiers,
        [NotNullWhen(true)] out ModifierKeyConsoleInputEvent? inputEvent)
    {
        if (_heldModifiers == modifiers)
        {
            inputEvent = null;
            return false;
        }

        _heldModifiers = modifiers;
        _pendingModifierReset = false;
        inputEvent = new ModifierKeyConsoleInputEvent(modifiers);
        return true;
    }

    private void ResetHeldModifiers()
    {
        if (_heldModifiers == default)
            return;

        _heldModifiers = default;
        _pendingModifierReset = true;
    }

    private bool TryReadParsedEvent(
        bool intercept,
        [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        if (!_parser.TryRead(_input, KittyTracksModifiers, out inputEvent, out bool? focusChanged))
        {
            if (focusChanged.HasValue)
            {
                _hasFocus = focusChanged.Value;
                if (!focusChanged.Value)
                    ResetHeldModifiers();
            }

            return false;
        }

        if (inputEvent is ModifierKeyConsoleInputEvent modifier)
        {
            if (!_hasFocus || !KittyTracksModifiers || !TryPublishModifiers(modifier.Modifiers, out var changed))
            {
                inputEvent = null;
                return false;
            }

            inputEvent = changed;
            return true;
        }

        // A chord describes that key event, not a physical modifier lifecycle.
        // Never change the held state or the function bar from its modifiers.
        if (!intercept && inputEvent is KeyConsoleInputEvent keyEvent)
        {
            string? text = keyEvent.Text ??
                (keyEvent.Key.KeyChar == '\0' ? null : keyEvent.Key.KeyChar.ToString());
            if (text is not null)
                _writeControl(text);
        }

        return true;
    }

    private void DisableFocus()
    {
        if (!_focusReportingActive)
            return;

        _writeControl(DisableFocusReporting);
        _focusReportingActive = false;
    }

    private void EnableInputMode()
    {
        if (_active)
            return;

        _terminalMode.EnableRawMode();
        try
        {
            _parser.ResetMouseState();
            _hasFocus = true;
            ResetHeldModifiers();
            _keyboardProtocol.Resume();
            if (KittyTracksModifiers)
                _modifierKeyTracker?.Suspend();
            else
                _modifierKeyTracker?.Resume();
            _writeControl(EnableFocusReporting);
            _focusReportingActive = true;
            if (_mouseTrackingRequested)
                ApplyMouseTracking(enabled: true);
            _active = true;
        }
        catch
        {
            _keyboardProtocol.Suspend();
            try
            {
                DisableFocus();
            }
            catch
            {
                // Preserve the original input-mode restoration failure.
            }
            _modifierKeyTracker?.Suspend();
            if (_mouseTrackingRequested)
            {
                try
                {
                    _writeControl(DisableMouseTracking);
                }
                catch
                {
                }

                _mouseTrackingActive = false;
            }

            _terminalMode.RestoreOriginalMode();
            throw;
        }
    }

    private void ApplyMouseTracking(bool enabled)
    {
        _parser.ResetMouseState();
        _writeControl(enabled ? EnableMouseTracking : DisableMouseTracking);
        _mouseTrackingActive = enabled;
    }

}
