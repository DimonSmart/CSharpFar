using CSharpFar.App.Dialogs;
using CSharpFar.App.State;
using CSharpFar.App.Updates;
using CSharpFar.Core.Abstractions;
namespace CSharpFar.App.Commands;

internal sealed class AboutCommand(DialogService dialogs, IUriLauncher uriLauncher, UpdateCheckService updateCheckService, IApplicationUpdateInstaller updateInstaller, ApplicationState state) : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.About;

    public bool CanExecute(object? args = null) => true;

    public ApplicationCommandResult Execute(object? args = null)
    {
        new AboutDialog(dialogs, uriLauncher, updateCheckService, updateInstaller,
            ApplicationVersionProvider.GetVersionInfo(), () => state.Running = false).Show();
        return ApplicationCommandResult.Rendered();
    }
}
