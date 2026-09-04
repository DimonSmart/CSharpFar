using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi;

/// <summary>
/// Reads raw ANSI terminal input through the backend's diagnostic parser without exposing parser internals.
/// </summary>
public sealed class AnsiInputDiagnosticReader
{
    private readonly AnsiTerminalConsoleDriver _driver;
    private readonly TerminalInputLabParser _parser = new();

    public AnsiInputDiagnosticReader(AnsiTerminalConsoleDriver driver)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    public bool TryRead(
        int timeoutMilliseconds,
        int escapeTimeoutMilliseconds,
        [NotNullWhen(true)] out AnsiInputDiagnosticEvent? inputEvent)
    {
        if (!_driver.TryReadRawInput(timeoutMilliseconds, escapeTimeoutMilliseconds, out var raw))
        {
            inputEvent = null;
            return false;
        }

        var parsed = _parser.Parse(raw.Bytes);
        inputEvent = new AnsiInputDiagnosticEvent(
            parsed.Kind,
            parsed.RawBytes,
            parsed.IsKnown,
            parsed.Key,
            parsed.MouseEvent,
            parsed.MouseButton,
            parsed.ButtonCode,
            parsed.TerminalX,
            parsed.TerminalY,
            parsed.UiX,
            parsed.UiY,
            parsed.MouseModifiers,
            parsed.KeyEventType?.ToString(),
            parsed.ModifierKeyName,
            parsed.Error);
        return true;
    }
}

/// <summary>
/// Parsed diagnostic view of one raw ANSI terminal input sequence.
/// </summary>
public sealed record AnsiInputDiagnosticEvent(
    string Kind,
    byte[] RawBytes,
    bool IsKnown,
    ConsoleKeyInfo? Key = null,
    string? MouseEvent = null,
    MouseButton? MouseButton = null,
    int? ButtonCode = null,
    int? TerminalX = null,
    int? TerminalY = null,
    int? UiX = null,
    int? UiY = null,
    MouseKeyModifiers MouseModifiers = MouseKeyModifiers.None,
    string? KeyEventType = null,
    string? ModifierKeyName = null,
    string? Error = null);
