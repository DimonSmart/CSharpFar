using CSharpFar.Console;
namespace CSharpFar.Ui;

public sealed class ModuleInputDialog
{
    private readonly ScreenRenderer _screen;

    public ModuleInputDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public string? Show(string title, string prompt, string? initialText)
    {
        var result = new SingleLineInputDialog(_screen).Show(new SingleLineInputDialogOptions
        {
            Title = title,
            Prompt = prompt,
            InitialText = initialText ?? string.Empty,
            AllowEmpty = true,
        });

        return result.IsConfirmed ? result.Text : null;
    }
}
