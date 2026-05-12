namespace CSharpFar.App.Commands;

internal sealed class OpenCurrentItemCommand : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.OpenCurrentItem;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var item = context.Controller.CurrentItem(context.ActiveState);
        if (item is not null)
            context.OpenPanelItem(context.ActiveState, context.ActiveSide, item);

        return ApplicationCommandResult.Rendered();
    }
}
