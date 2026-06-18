using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;

namespace CSharpFar.App.Commands;

internal sealed class CommandHistoryCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.CommandHistory;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        try
        {
            string? command = new HistoryDialog(context.Screen).Show(context.History.GetCommandHistory());
            if (command is not null)
                context.CommandLine.SetText(command);

            context.HideCommandCompletion(temporarily: false);
            context.ResetCommandHistoryNavigation();
            return ApplicationCommandResult.Rendered();
        }
        finally
        {
            context.ResetFunctionKeyLayer();
        }
    }
}
