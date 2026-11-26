using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.Panels;
using CSharpFar.App.Files;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class CommandHistoryCommand(DialogService dialogs, IHistoryStore history, CommandLineState commandLine, CommandCompletionController completion, CommandHistoryNavigator historyNavigator, Action resetFunctionKeyLayer) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.CommandHistory;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        try
        {
            var result = dialogs.Select(new SelectionDialogOptions<CommandHistoryItem>
            {
                Title = "Command History",
                Items = history.GetCommandHistory().Reverse().ToArray(),
                ItemText = static item => item.Command,
                Presentation = SelectionDialogPresentation.Standard,
            });
            string? command = result.IsConfirmed ? result.SelectedItem?.Command : null;
            if (command is not null)
                commandLine.SetText(command);

            completion.Hide(temporarily: false);
            historyNavigator.Reset();
            return ApplicationCommandResult.Rendered();
        }
        finally
        {
            resetFunctionKeyLayer();
        }
    }
}
