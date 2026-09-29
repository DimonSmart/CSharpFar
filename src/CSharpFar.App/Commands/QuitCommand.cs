using CSharpFar.App.FunctionKeys;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class QuitCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Quit;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!context.Settings.Application.ConfirmExit)
        {
            context.Running = false;
            return ApplicationCommandResult.NotRendered();
        }

        ChoiceDialogResult result = context.Dialogs.Choice(new ChoiceDialogOptions
        {
            Title = "Exit CSharpFar",
            Lines = ["Exit CSharpFar?"],
            Buttons =
            [
                DialogButton.Action("exit", "Exit", 'E'),
                DialogButton.Cancel("Cancel", 'C'),
            ],
            DefaultButtonIndex = 1,
            CancelButtonIndex = 1,
        });

        if (!string.Equals(result.ButtonId, "exit", StringComparison.Ordinal))
            return ApplicationCommandResult.Rendered();

        context.Running = false;
        return ApplicationCommandResult.NotRendered();
    }
}
