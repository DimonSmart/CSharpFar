using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class SftpConnectCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SftpConnect;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        context.OpenSftpConnectionManager(context.ActiveSide);
        return ApplicationCommandResult.Rendered();
    }
}
