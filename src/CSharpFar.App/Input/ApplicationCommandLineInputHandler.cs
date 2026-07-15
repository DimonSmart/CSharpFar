using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class ApplicationCommandLineInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationCommandLineInputHandler(MouseInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(
        MouseConsoleInputEvent input,
        ApplicationCommandLineFrame frame,
        UiInputRouteKind routeKind)
    {
        bool captured = routeKind == UiInputRouteKind.CapturedTarget;
        bool hit = routeKind == UiInputRouteKind.HitTarget;

        if (input.Button == MouseButton.Left && input.Kind == MouseEventKind.Down && hit)
        {
            _context.CommandLine.MoveCursorTo(frame.TextPositionFromX(input.X));
            _context.ResetCommandHistoryNavigation();
            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        if (input.Button == MouseButton.Left && input.Kind == MouseEventKind.Move && captured)
        {
            _context.CommandLine.MoveCursorWithSelection(frame.TextPositionFromX(input.X));
            _context.ResetCommandHistoryNavigation();
            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        if (input.Button == MouseButton.Left && input.Kind == MouseEventKind.Up && captured)
            return ApplicationInputHandlingResult.FromHandled(shouldRender: false);

        if (input.Button == MouseButton.Left && input.Kind == MouseEventKind.DoubleClick && hit)
        {
            SelectWordAt(frame.TextPositionFromX(input.X));
            _context.ResetCommandHistoryNavigation();
            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        if (input.Button == MouseButton.Right && input.Kind == MouseEventKind.Down && hit)
            return ApplicationInputHandlingResult.FromHandled(_context.PasteTextIntoCommandLine());

        return captured
            ? ApplicationInputHandlingResult.FromHandled(shouldRender: false)
            : ApplicationInputHandlingResult.NotHandled;
    }

    private void SelectWordAt(int position)
    {
        CommandLineState commandLine = _context.CommandLine;
        string text = commandLine.Text;
        if (text.Length == 0)
            return;

        position = Math.Clamp(position, 0, text.Length - 1);
        if (char.IsWhiteSpace(text[position]) && position > 0 && !char.IsWhiteSpace(text[position - 1]))
            position--;

        int start = position;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;

        int end = position;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;

        commandLine.MoveCursorTo(start);
        commandLine.MoveCursorWithSelection(end);
    }
}
