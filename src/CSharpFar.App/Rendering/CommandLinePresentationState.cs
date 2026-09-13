using CSharpFar.App.CommandLine;

namespace CSharpFar.App.Rendering;

internal static class CommandLinePresentationState
{
    public static SingleLineTextEditState Create(string currentDirectory, CommandLineState source)
    {
        ArgumentNullException.ThrowIfNull(currentDirectory);
        ArgumentNullException.ThrowIfNull(source);

        string prompt = currentDirectory + ">";
        var presentation = new SingleLineTextEditState();
        presentation.SetText(prompt + CommandLineDisplayText.Format(source.Text));

        int textOffset = prompt.Length;
        if (!source.HasSelection)
        {
            presentation.MoveCursorTo(textOffset + source.CursorPosition);
            return presentation;
        }

        int start = textOffset + source.SelectionStart!.Value;
        int end = start + source.SelectionLength;
        if (source.CursorPosition == source.SelectionStart.Value)
        {
            presentation.MoveCursorTo(end);
            presentation.MoveCursorWithSelection(start);
        }
        else
        {
            presentation.MoveCursorTo(start);
            presentation.MoveCursorWithSelection(end);
        }

        return presentation;
    }
}
