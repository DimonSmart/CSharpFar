using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Viewer;

namespace CSharpFar.App.Commands;

internal sealed class HelpCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Help;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        new HelpViewer(context.Screen, context.Palette).Show();
        return ApplicationCommandResult.Rendered();
    }
}
