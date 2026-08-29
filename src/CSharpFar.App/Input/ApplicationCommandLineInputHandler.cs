using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Input;

internal sealed class ApplicationCommandLineInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationCommandLineInputHandler(MouseInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ApplicationCommandLineInteraction interaction)
    {
        switch (interaction.Action.Kind)
        {
            case RoutedPointerSelectionActionKind.SelectionStarted:
                _context.CommandLine.MoveCursorTo(interaction.Action.Position);
                _context.ResetCommandHistoryNavigation();
                return CommandLineChanged();
            case RoutedPointerSelectionActionKind.SelectionExtended:
                _context.CommandLine.MoveCursorWithSelection(interaction.Action.Position);
                _context.ResetCommandHistoryNavigation();
                return CommandLineChanged();
            case RoutedPointerSelectionActionKind.SelectionCompleted:
                return ApplicationInputHandlingResult.FromHandled(shouldRender: false, resumesHiddenInteraction: false);
            case RoutedPointerSelectionActionKind.WordSelectionRequested:
                SelectWordAt(interaction.Action.Position);
                _context.ResetCommandHistoryNavigation();
                return CommandLineChanged();
            case RoutedPointerSelectionActionKind.SecondaryActionRequested:
                return CommandLineChanged(_context.PasteTextIntoCommandLine());
            default:
                return ApplicationInputHandlingResult.NotHandled;
        }
    }

    private static ApplicationInputHandlingResult CommandLineChanged(bool changed = true) =>
        ApplicationInputHandlingResult.FromHandled(
            changed,
            ApplicationRenderPart.CommandLine | ApplicationRenderPart.Completion);

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
