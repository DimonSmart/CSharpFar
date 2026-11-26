using CSharpFar.App.Dialogs;
using CSharpFar.App.State;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class QuitCommand(AppSettingsAlias settings, DialogService dialogs, ApplicationState state) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Quit;

    public bool CanExecute(object? args = null) => true;

    public ApplicationCommandResult Execute(object? args = null)
    {
        if (!settings.Application.ConfirmExit)
        {
            state.Running = false;
            return ApplicationCommandResult.NotRendered();
        }

        ChoiceDialogResult result = dialogs.Choice(new ChoiceDialogOptions
        {
            Title = "Exit CSharpFar",
            Lines = ["Are you sure?"],
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

        state.Running = false;
        return ApplicationCommandResult.NotRendered();
    }
}
