using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.State;
using CSharpFar.App.Rendering;
using CSharpFar.App.Panels;
using CSharpFar.App.Modules;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class UserMenuCommand(PanelCommandTargetResolver targetResolver, PanelController controller, PanelWorkspaceController workspace, FilePanelSourceRegistry sourceRegistry, UserMenuStore userMenu, DialogService dialogs, FormFieldFactory fields, CommandLineCommandExecutor commandExecutor, bool globalOnly = false) : IApplicationCommand
{
    public string CommandId => globalOnly ? FunctionKeyCommandIds.GlobalUserMenu : FunctionKeyCommandIds.UserMenu;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!PanelCommandUserMenuOperands.TryCreate(target, controller, out var operands))
            return ApplicationCommandResult.Rendered();

        PlatformKind platform = userMenu.RuntimePlatform
            ?? throw new InvalidOperationException("User menu runtime platform is not configured.");

        ResolvedUserMenu menu;
        try
        {
            menu = UserMenuResolver.Resolve(
                userMenu, platform,
                !globalOnly && PanelCommandUserMenuOperands.CanUseLocalUserMenu(sourceRegistry, target) ? operands.ActiveDirectory : null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            dialogs.Message("User Menu", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        ResolvedUserMenuItem[] items = menu.Sections.SelectMany(section => section.Items).ToArray();
        if (items.Length == 0)
        {
            dialogs.Message(
                "User Menu",
                menu.HasLocalMenus
                    ? "No commands are available for this platform."
                    : "User menu is empty.\nConfigure it in Options → User menu...");
            return ApplicationCommandResult.Rendered();
        }

        ResolvedUserMenuItem? chosen;
        if (!menu.HasLocalMenus)
        {
            var selection = dialogs.Select(new SelectionDialogOptions<ResolvedUserMenuItem>
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
                string location = section.SourcePath == userMenu.FilePath
                    ? "Global"
                    : section.Label + "  [" + Path.GetDirectoryName(section.SourcePath) + "]";
                rows.Add(new UserMenuRow("── " + location, null));
                rows.AddRange(section.Items.Select(item => new UserMenuRow("    " + item.Item.Title, item)));
            }

            var selection = dialogs.Select(new SelectionDialogOptions<UserMenuRow>
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

        if (workspace.ActiveSide != target.Side ||
            workspace.GetPanelState(target.Side).CurrentDirectory != operands.ActiveDirectory ||
            workspace.GetPanelState(target.PassiveSide).CurrentDirectory != operands.PassiveDirectory ||
            !PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted) ||
            !PanelCommandTargetResolver.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted))
        {
            return ApplicationCommandResult.Rendered();
        }

        string expanded;
        try
        {
            CommandTemplate template = CommandTemplateParser.Parse(UserMenuItemRules.EffectiveCommand(chosen.Item));
            CommandTemplateRenderer.ValidatePlacement(template, platform);

            IReadOnlyDictionary<string, string> values = new Dictionary<string, string>();
            if (template.Parameters.Count > 0)
            {
                CommandParameterDialogResult? input = new CommandParameterDialog(dialogs, fields)
                    .Show(chosen.Item.Title, template.Parameters, candidate =>
                    {
                        try
                        {
                            _ = CommandTemplateRenderer.Expand(
                                template, candidate, literal => operands.Expand(literal, chosen.MenuDirectory),
                                CommandTemplateRenderer.Dialect(template.Original, platform));
                            return null;
                        }
                        catch (ArgumentException ex)
                        {
                            return ex.Message;
                        }
                    });
                if (input is null)
                    return ApplicationCommandResult.Rendered();
                values = input.Values;
            }

            expanded = CommandTemplateRenderer.Expand(
                template, values,
                literal => operands.Expand(literal, chosen.MenuDirectory),
                CommandTemplateRenderer.Dialect(template.Original, platform));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            dialogs.Message("User Menu", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        commandExecutor.Execute(expanded);
        return ApplicationCommandResult.Rendered();
    }

    private sealed record UserMenuRow(string Text, ResolvedUserMenuItem? Command);
}
