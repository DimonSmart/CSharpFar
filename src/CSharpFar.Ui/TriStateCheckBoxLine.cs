using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class TriStateCheckBoxLine
{
    public TriStateCheckBoxLine(string label, AttributeEditState value = AttributeEditState.Unchecked, bool enabled = true)
    {
        Label = label;
        Value = value;
        Enabled = enabled;
    }

    public string Label { get; }
    public AttributeEditState Value { get; set; }
    public bool Enabled { get; set; }

    public void Render(ScreenRenderer screen, int x, int y, int width, bool focused)
    {
        ArgumentNullException.ThrowIfNull(screen);

        char marker = Value switch
        {
            AttributeEditState.Checked => 'x',
            AttributeEditState.Indeterminate => '-',
            _ => ' ',
        };
        string text = $"[{marker}] {Label}";
        CellStyle style = focused && Enabled
            ? FarDialogStyles.FocusedInput
            : FarDialogStyles.Fill;
        screen.Write(x, y, Fit(text, width), style);
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
            AttributeEditState.Indeterminate => AttributeEditState.Checked,
            AttributeEditState.Unchecked => AttributeEditState.Checked,
            _ => AttributeEditState.Unchecked,
        };

    private static bool Contains(Rect bounds, int x, int y) =>
        x >= bounds.X && x < bounds.Right && y >= bounds.Y && y < bounds.Bottom;

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
