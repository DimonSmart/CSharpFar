using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class CommandHistoryCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.CommandHistory;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        try
        {
            var result = context.Dialogs.Select(new SelectionDialogOptions<CommandHistoryItem>
            {
                Title = "Command History",
                Items = context.History.GetCommandHistory(),
                ItemText = static item => item.Command,
            });
            string? command = result.IsConfirmed ? result.SelectedItem?.Command : null;
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
