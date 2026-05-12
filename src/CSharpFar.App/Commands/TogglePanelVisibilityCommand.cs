using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal abstract class TogglePanelVisibilityCommand : IApplicationCommand
{
    protected TogglePanelVisibilityCommand(string commandId, PanelSide panelSide)
    {
        CommandId = commandId;
        PanelSide = panelSide;
    }

    public string CommandId { get; }

    protected PanelSide PanelSide { get; }

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null) =>
        context.TogglePanelVisibility(PanelSide)
            ? ApplicationCommandResult.Rendered()
            : ApplicationCommandResult.NotRendered();
}

internal sealed class ToggleLeftPanelVisibilityCommand : TogglePanelVisibilityCommand
{
    public ToggleLeftPanelVisibilityCommand()
        : base(FunctionKeyCommandIds.ToggleLeftPanel, PanelSide.Left)
    {
    }
}

internal sealed class ToggleRightPanelVisibilityCommand : TogglePanelVisibilityCommand
{
    public ToggleRightPanelVisibilityCommand()
        : base(FunctionKeyCommandIds.ToggleRightPanel, PanelSide.Right)
    {
    }
}
