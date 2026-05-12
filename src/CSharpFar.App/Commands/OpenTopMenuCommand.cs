using CSharpFar.App.FunctionKeys;

namespace CSharpFar.App.Commands;

internal sealed class OpenTopMenuCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.TopMenu;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null) =>
        context.OpenTopMenu()
            ? ApplicationCommandResult.Rendered()
            : ApplicationCommandResult.NotRendered();
}
