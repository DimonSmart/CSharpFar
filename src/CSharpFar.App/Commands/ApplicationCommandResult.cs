using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed record ApplicationCommandResult(
    bool Success,
    bool ShouldRender,
    string? ErrorMessage = null)
{
    public static ApplicationCommandResult Rendered() => new(true, true);

    public static ApplicationCommandResult NotRendered() => new(true, false);

    public static ApplicationCommandResult Failure(string errorMessage) =>
        new(false, true, errorMessage);

    public MenuCommandResult ToMenuCommandResult() =>
        new()
        {
            Success = Success,
            ErrorMessage = ErrorMessage,
        };
}
