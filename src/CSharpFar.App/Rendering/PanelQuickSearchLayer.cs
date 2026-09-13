using CSharpFar.App.State;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed class PanelQuickSearchLayer : TransientTextPromptLayer
{
    public PanelQuickSearchLayer(
        ApplicationRenderContext context,
        Action<bool> hideCompletion,
        Action resetHistoryNavigation)
        : base(CreateDefinition(context, hideCompletion, resetHistoryNavigation))
    {
    }

    private static TransientTextPromptDefinition CreateDefinition(
        ApplicationRenderContext context,
        Action<bool> hideCompletion,
        Action resetHistoryNavigation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hideCompletion);
        ArgumentNullException.ThrowIfNull(resetHistoryNavigation);

        CellStyle inputStyle = CSharpFarPaletteStyles.InputField(context.App.Palette);
        return new TransientTextPromptDefinition
        {
            Id = "application.panel-quick-search",
            IsActive = () =>
                context.PanelQuickSearch.State is not null &&
                context.App.WorkspaceMode == ApplicationWorkspaceMode.Panels,
            Text = () => context.PanelQuickSearch.State?.SearchText ?? string.Empty,
            TextChanged = text => context.PanelQuickSearch.SetText(text),
            Title = "Search",
            Placement = new TransientPopupPlacement
            {
                Anchor = size =>
                {
                    var workspace = ApplicationLayoutService.CalculatePanelWorkspaceBounds(size);
                    return context.PanelQuickSearch.State?.PanelSide == PanelSide.Right
                        ? workspace.Right
                        : workspace.Left;
                },
                Mode = TransientPopupPlacementMode.CenteredBottomInsideAnchor,
                PreferredWidth = 34,
                MinimumWidth = 10,
                HorizontalInset = 2,
                VerticalInset = 1,
            },
            Appearance = new TransientTextPromptAppearance(
                CSharpFarPaletteStyles.DialogPopupOptions(context.App.Palette) with { DrawShadow = false },
                inputStyle,
                new CellStyle(inputStyle.Background, inputStyle.Foreground)),
            TryActivate = key =>
            {
                if ((key.Modifiers & ConsoleModifiers.Alt) == 0 ||
                    (key.Modifiers & ConsoleModifiers.Control) != 0 ||
                    !context.PanelQuickSearch.TryStart(key))
                {
                    return false;
                }

                hideCompletion(false);
                resetHistoryNavigation();
                return true;
            },
            AdditionalCharacter = AltLetter,
            CharacterFilter = IsFilenameCharacter,
            Cancelled = context.PanelQuickSearch.Close,
            DismissOnUnhandledKey = true,
            DismissOnOutsideClick = true,
            BubbleOutsideClick = true,
        };
    }

    private static char? AltLetter(ConsoleKeyInfo key)
    {
        if ((key.Modifiers & ConsoleModifiers.Alt) == 0 ||
            (key.Modifiers & ConsoleModifiers.Control) != 0 ||
            key.Key is < ConsoleKey.A or > ConsoleKey.Z)
        {
            return null;
        }

        return (char)('a' + (int)key.Key - (int)ConsoleKey.A);
    }

    private static bool IsFilenameCharacter(char value) =>
        !char.IsControl(value) && Array.IndexOf(Path.GetInvalidFileNameChars(), value) < 0;
}
