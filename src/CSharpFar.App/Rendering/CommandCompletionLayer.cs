using CSharpFar.App.CommandLine;
using CSharpFar.App.Input;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed class CommandCompletionLayer : TransientSelectionPopupLayer<string>
{
    public CommandCompletionLayer(
        ApplicationRenderContext context,
        CommandCompletionController controller,
        Action<bool> hideCompletion,
        Action resetHistoryNavigation)
        : base(CreateDefinition(context, controller, hideCompletion, resetHistoryNavigation))
    {
    }

    internal static Rect? CalculatePopupBounds(ConsoleSize size, int itemCount) =>
        CreatePlacement().CalculateSelectionBounds(size, itemCount);

    protected override UiInputResult RouteInput(
        ConsoleInputEvent input,
        TransientSelectionPopupFrame<string> frame,
        UiInputRouteContext context)
    {
        if (input is KeyConsoleInputEvent { Key: var key })
        {
            bool supported = key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or
                ConsoleKey.Delete or ConsoleKey.Escape ||
                KeyboardShortcutClassifier.IsPlainEnter(key);
            if (!supported)
                return UiInputResult.NotHandled;
        }

        return base.RouteInput(input, frame, context);
    }

    private static TransientSelectionPopupDefinition<string> CreateDefinition(
        ApplicationRenderContext context,
        CommandCompletionController controller,
        Action<bool> hideCompletion,
        Action resetHistoryNavigation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(hideCompletion);
        ArgumentNullException.ThrowIfNull(resetHistoryNavigation);

        CommandCompletionState completion = context.CommandCompletion;
        return new TransientSelectionPopupDefinition<string>
        {
            Id = "application.command-completion",
            IsVisible = () =>
                completion.Visible &&
                (context.App.WorkspaceMode != ApplicationWorkspaceMode.HiddenCommandLine ||
                 !context.Ui.HiddenUiDetachedByScroll),
            Items = () => completion.Items,
            ItemText = static text => text.Replace('\n', '↵'),
            SelectedIndex = () => completion.SelectedIndex,
            SelectionChanged = index => completion.SelectedIndex = index,
            ItemIdentity = static text => text,
            Placement = CreatePlacement(),
            Appearance = new TransientSelectionPopupAppearance(
                CSharpFarPaletteStyles.DialogPopupOptions(context.App.Palette) with { DrawShadow = false },
                CSharpFarPaletteStyles.DialogFill(context.App.Palette),
                CSharpFarPaletteStyles.InputField(context.App.Palette),
                CSharpFarPaletteStyles.DialogFill(context.App.Palette)),
            ActivateOnMouseDown = true,
            FocusList = false,
            Dismissed = () => hideCompletion(true),
            KeyboardCommands = new Dictionary<ConsoleKey, string>
            {
                [ConsoleKey.Delete] = "delete",
            },
            Command = command =>
            {
                if (command.Command != "delete")
                    return TransientPopupAction.KeepOpen;

                if (command.SelectedIndex <= 0 ||
                    context.CommandLine.HasSelection ||
                    context.CommandLine.CursorPosition != context.CommandLine.Text.Length)
                {
                    return TransientPopupAction.DismissAndContinue;
                }

                if (!controller.TryRemoveSelectedCommand(context.CommandLine, command.SelectedIndex))
                    return TransientPopupAction.KeepOpen;

                resetHistoryNavigation();
                return TransientPopupAction.Refresh;
            },
            Activated = activation =>
            {
                if (activation.Index == 0)
                {
                    hideCompletion(false);
                    resetHistoryNavigation();
                    return activation.Source == TransientSelectionActivationSource.Keyboard
                        ? TransientPopupAction.DismissAndContinue
                        : TransientPopupAction.Dismiss;
                }

                context.CommandLine.SetText(activation.Item);
                hideCompletion(false);
                resetHistoryNavigation();
                return TransientPopupAction.Dismiss;
            },
        };
    }

    private static TransientPopupPlacement CreatePlacement() => new()
    {
        Anchor = size => new Rect(0, ApplicationLayoutService.CommandLineRow(size), size.Width, 1),
        Mode = TransientPopupPlacementMode.AboveAnchor,
        PreferredWidth = 0,
        MinimumWidth = 1,
        MaxVisibleRows = 8,
        ReservedRowsAbove = 2,
    };
}
