namespace CSharpFar.App.Commands;

internal sealed class DiagnosticsCommand : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.Diagnostics;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        context.ShowDiagnostics();
        return ApplicationCommandResult.Rendered();
    }
}
