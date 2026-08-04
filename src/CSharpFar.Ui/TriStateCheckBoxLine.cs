using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

internal sealed class TriStateCheckBoxLine
{
    internal TriStateCheckBoxLine(string label, CheckState value = CheckState.Unchecked, bool enabled = true)
    {
        Label = label;
        Value = value;
        Enabled = enabled;
    }

    public string Label { get; }
    public CheckState Value { get; set; }
    public bool Enabled { get; set; }

    public void Render(
        IUiCanvas screen,
        int x,
        int y,
        int width,
        bool focused,
        CellStyle fillStyle,
        CellStyle focusedStyle,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(screen);

        char marker = Value switch
        {
            CheckState.Checked => 'x',
            CheckState.Indeterminate => '-',
            _ => ' ',
        };
        string text = $"[{marker}] {label ?? Label}";
        screen.Write(x, y, Fit(text, width), focused ? focusedStyle : fillStyle);
    }

    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (!Enabled || key.Key is not (ConsoleKey.Spacebar or ConsoleKey.Enter))
            return false;

        Toggle();
        return true;
    }

    public bool TryHandleMouse(MouseConsoleInputEvent mouse, Rect bounds)
    {
        if (!Enabled ||
            mouse.Button != MouseButton.Left ||
            mouse.Kind != MouseEventKind.Down ||
            !Contains(bounds, mouse.X, mouse.Y))
        {
            return false;
        }

        Toggle();
        return true;
    }

    private void Toggle() =>
        Value = Value switch
        {
            CheckState.Indeterminate => CheckState.Checked,
            CheckState.Unchecked => CheckState.Checked,
            _ => CheckState.Unchecked,
        };

    private static bool Contains(Rect bounds, int x, int y) =>
        x >= bounds.X && x < bounds.Right && y >= bounds.Y && y < bounds.Bottom;

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        return ConsoleTextMetrics.FitToCells(text, width);
    }
}
