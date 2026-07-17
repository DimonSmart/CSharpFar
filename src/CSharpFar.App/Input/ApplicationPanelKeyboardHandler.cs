using CSharpFar.App.Commands;
using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Input;

internal sealed class ApplicationPanelKeyboardHandler
{
    private readonly KeyboardInputContext _context;

    public ApplicationPanelKeyboardHandler(KeyboardInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ApplicationKeyboardInput input, PanelSide side, ApplicationPanelFrame? frame)
    {
        ConsoleKeyInfo key = input.Key;
        if (frame is null || frame.Side != side)
            return ApplicationInputHandlingResult.NotHandled;

        FilePanelState state = StateForSide(side);
        int visibleRows = frame.VisibleRows;

        bool isControlShortcut =
            (key.Modifiers & ConsoleModifiers.Control) != 0 &&
            (key.Modifiers & ConsoleModifiers.Alt) == 0;
        if (isControlShortcut)
        {
            switch (key.Key)
            {
                case ConsoleKey.Multiply:
                    _context.PanelController.InvertSelection(state, _context.PanelOptions());
                    return ApplicationInputHandlingResult.FromHandled(true);
                case ConsoleKey.D8 when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                    _context.PanelController.InvertSelection(state, _context.PanelOptions());
                    return ApplicationInputHandlingResult.FromHandled(true);
            }
        }

        if (key.Modifiers != 0)
            return ApplicationInputHandlingResult.NotHandled;

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                _context.PanelController.MoveCursorByColumn(
                    state,
                    -1,
                    frame.RowsPerColumn,
                    frame.ColumnCount,
                    visibleRows);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.RightArrow:
                _context.PanelController.MoveCursorByColumn(
                    state,
                    +1,
                    frame.RowsPerColumn,
                    frame.ColumnCount,
                    visibleRows);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.Home:
                _context.PanelController.MoveToFirst(state);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.End:
                _context.PanelController.MoveToLast(state, visibleRows);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.Backspace:
                _context.HideCommandCompletion(false);
                _context.TryGoUp(state, side);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.Escape:
                if (frame.Keyboard.HasSearchRequest)
                    _context.CloseSearchResultsPanel(state, side);
                else
                {
                    _context.CommandLine.Clear();
                    _context.HideCommandCompletion(false);
                }
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.Enter:
                if (ApplicationCommandContext.TryResolveCommittedCurrentItem(state, frame.Keyboard, out var item))
                    _context.OpenPanelItem(state, side, item);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.Insert:
                _context.PanelController.ToggleSelection(state, visibleRows, _context.PanelOptions());
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.Tab:
                _context.SetActiveSide(OtherPanelSide(side));
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.UpArrow:
                _context.PanelController.MoveCursor(state, -1, visibleRows);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.DownArrow:
                _context.PanelController.MoveCursor(state, +1, visibleRows);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.PageUp:
                _context.PanelController.MoveCursor(state, -visibleRows, visibleRows);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.PageDown:
                _context.PanelController.MoveCursor(state, +visibleRows, visibleRows);
                return ApplicationInputHandlingResult.FromHandled(true);
        }

        return ApplicationInputHandlingResult.NotHandled;
    }

    private static PanelSide OtherPanelSide(PanelSide side) =>
        side == PanelSide.Left ? PanelSide.Right : PanelSide.Left;

    private FilePanelState StateForSide(PanelSide side) =>
        side == PanelSide.Left ? _context.LeftPanel() : _context.RightPanel();

}
