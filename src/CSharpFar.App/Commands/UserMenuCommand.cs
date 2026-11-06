using CSharpFar.App.FunctionKeys;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class UserMenuCommand : IApplicationCommand
{
    private readonly bool _globalOnly;

    public UserMenuCommand(bool globalOnly = false) => _globalOnly = globalOnly;

    public string CommandId => _globalOnly ? FunctionKeyCommandIds.GlobalUserMenu : FunctionKeyCommandIds.UserMenu;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!PanelCommandUserMenuOperands.TryCreate(target, context, out var operands))
            return ApplicationCommandResult.Rendered();

        PlatformKind platform = context.UserMenu.RuntimePlatform
            ?? throw new InvalidOperationException("User menu runtime platform is not configured.");

        ResolvedUserMenu menu;
        try
        {
            menu = UserMenuResolver.Resolve(
                context.UserMenu, platform,
                !_globalOnly && context.CanUseLocalUserMenu(target) ? operands.ActiveDirectory : null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            context.Dialogs.Message("User Menu", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        ResolvedUserMenuItem[] items = menu.Sections.SelectMany(section => section.Items).ToArray();
        if (items.Length == 0)
        {
            context.Dialogs.Message(
                "User Menu",
                menu.HasLocalMenus
                    ? "No commands are available for this platform."
                    : "User menu is empty.\nConfigure it in Options → User menu...");
            return ApplicationCommandResult.Rendered();
        }

        ResolvedUserMenuItem? chosen;
        if (!menu.HasLocalMenus)
        {
            var selection = context.Dialogs.Select(new SelectionDialogOptions<ResolvedUserMenuItem>
            {
                Title = "User Menu",
                Items = items,
                ItemText = static item => item.Item.Title,
                Presentation = SelectionDialogPresentation.Standard,
                Appearance = DialogAppearance.Standard,
                DoubleBorder = true,
            });
            chosen = selection.IsConfirmed ? selection.SelectedItem : null;
        }
        else
        {
            var rows = new List<UserMenuRow>();
            foreach (ResolvedUserMenuSection section in menu.Sections)
            {
                string location = section.SourcePath == context.UserMenu.FilePath
                    ? "Global"
                    : section.Label + "  [" + Path.GetDirectoryName(section.SourcePath) + "]";
                rows.Add(new UserMenuRow("── " + location, null));
                rows.AddRange(section.Items.Select(item => new UserMenuRow("    " + item.Item.Title, item)));
            }

            var selection = context.Dialogs.Select(new SelectionDialogOptions<UserMenuRow>
            {
                Title = "User Menu — " + operands.ActiveDirectory,
                Items = rows,
                ItemText = static row => row.Text,
                IsSelectable = static row => row.Command is not null,
                Presentation = new SelectionDialogPresentation(MaxWidth: 100, MaxVisibleRows: 18),
                Appearance = DialogAppearance.Standard,
                DoubleBorder = true,
            });
            chosen = selection.IsConfirmed ? selection.SelectedItem?.Command : null;
        }

        if (chosen is null)
            return ApplicationCommandResult.Rendered();

        if (context.ActiveSide != target.Side ||
            context.GetPanelState(target.Side).CurrentDirectory != operands.ActiveDirectory ||
            context.GetPanelState(target.PassiveSide).CurrentDirectory != operands.PassiveDirectory ||
            !ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted) ||
            !ApplicationCommandContext.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted))
        {
            return ApplicationCommandResult.Rendered();
        }

        string expanded = operands.Expand(UserMenuItemRules.EffectiveCommand(chosen.Item), chosen.MenuDirectory);
        context.ExecuteCommand(expanded);
        return ApplicationCommandResult.Rendered();
    }

    private sealed record UserMenuRow(string Text, ResolvedUserMenuItem? Command);
}
