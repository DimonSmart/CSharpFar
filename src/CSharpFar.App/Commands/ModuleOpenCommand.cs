using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed record ModuleOpenCommandArgs(Guid ActionId);

internal sealed class ModuleOpenCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.ModuleOpen;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        args is ModuleOpenCommandArgs;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (args is not ModuleOpenCommandArgs moduleArgs)
            return ApplicationCommandResult.Failure("Module command arguments are required.");

        return context.OpenModuleMenuItem(moduleArgs.ActionId);
    }
}
