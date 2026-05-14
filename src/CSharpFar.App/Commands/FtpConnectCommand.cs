using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class FtpConnectCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.FtpConnect;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        context.OpenFtpConnectionManager(context.ActiveSide);
        return ApplicationCommandResult.Rendered();
    }
}
