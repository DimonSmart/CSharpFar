using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Panels;

internal sealed class PanelQuickSearchController
{
    private readonly PanelController _panelController;
    private readonly Func<PanelSide> _activeSide;
    private readonly Func<bool> _hasVisiblePanels;
    private readonly Func<PanelSide, bool> _isPanelVisible;
    private readonly Func<PanelSide, FilePanelState> _getPanelState;
    private readonly Func<PanelSide, int> _visibleRows;

    public PanelQuickSearchController(
        PanelController panelController,
        Func<PanelSide> activeSide,
        Func<bool> hasVisiblePanels,
        Func<PanelSide, bool> isPanelVisible,
        Func<PanelSide, FilePanelState> getPanelState,
        Func<PanelSide, int> visibleRows)
    {
        _panelController = panelController;
        _activeSide = activeSide;
        _hasVisiblePanels = hasVisiblePanels;
        _isPanelVisible = isPanelVisible;
        _getPanelState = getPanelState;
        _visibleRows = visibleRows;
    }

    public PanelQuickSearchState? State { get; private set; }

    public void Close() => State = null;

    public void CloseForPanel(PanelSide side)
    {
        if (State?.PanelSide == side)
            Close();
    }

    public void CloseForState(FilePanelState state) =>
        CloseForPanel(PanelSideForState(state));

    public PanelQuickSearchKeyResult HandleKey(ConsoleKeyInfo key)
    {
        if (State is not { } quickSearch)
            return PanelQuickSearchKeyResult.NotHandled;

        if (!_hasVisiblePanels() ||
            !_isPanelVisible(quickSearch.PanelSide) ||
            quickSearch.PanelSide != _activeSide())
        {
            Close();
            return PanelQuickSearchKeyResult.NotHandled;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            Close();
            return PanelQuickSearchKeyResult.Handled;
        }

        if (key.Key == ConsoleKey.Backspace &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0)
        {
            if (quickSearch.RemoveLastCharacter())
                MoveCursor();
            else
                Close();

            return PanelQuickSearchKeyResult.Handled;
        }

        if (TryGetAppendCharacter(key, out char ch))
        {
            quickSearch.Append(ch);
            MoveCursor();
            return PanelQuickSearchKeyResult.Handled;
        }

        Close();
        return PanelQuickSearchKeyResult.CloseAndContinue;
    }

    public bool TryStart(ConsoleKeyInfo key)
    {
        if (!TryGetActivationCharacter(key, out char ch))
            return false;

        State = new PanelQuickSearchState(_activeSide(), ch);
        MoveCursor();
        return true;
    }

    private bool TryGetActivationCharacter(ConsoleKeyInfo key, out char ch)
    {
        ch = default;
        var activeSide = _activeSide();
        if (!_hasVisiblePanels() ||
            !_isPanelVisible(activeSide) ||
            (key.Modifiers & ConsoleModifiers.Alt) == 0 ||
            (key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            return false;
        }

        if (key.Key is ConsoleKey.D1 or ConsoleKey.NumPad1 or ConsoleKey.D2 or ConsoleKey.NumPad2 ||
            key.Key is >= ConsoleKey.F1 and <= ConsoleKey.F24)
        {
            return false;
        }

        return TryGetCharacterFromKeyInfo(key, out ch) ||
               TryGetAltLetterCharacter(key, out ch);
    }

    private static bool TryGetAppendCharacter(ConsoleKeyInfo key, out char ch)
    {
        ch = default;
        if ((key.Modifiers & ConsoleModifiers.Control) != 0 ||
            key.Key is ConsoleKey.D1 or ConsoleKey.NumPad1 or ConsoleKey.D2 or ConsoleKey.NumPad2 ||
            key.Key is >= ConsoleKey.F1 and <= ConsoleKey.F24)
        {
            return false;
        }

        return TryGetCharacterFromKeyInfo(key, out ch) ||
               ((key.Modifiers & ConsoleModifiers.Alt) != 0 &&
                TryGetAltLetterCharacter(key, out ch));
    }

    private static bool TryGetCharacterFromKeyInfo(ConsoleKeyInfo key, out char ch)
    {
        ch = default;
        if (key.KeyChar < ' ' || !IsFilenameCharacter(key.KeyChar))
            return false;

        ch = char.ToLowerInvariant(key.KeyChar);
        return true;
    }

    private static bool TryGetAltLetterCharacter(ConsoleKeyInfo key, out char ch)
    {
        ch = default;
        if (key.Key is < ConsoleKey.A or > ConsoleKey.Z)
            return false;

        ch = (char)('a' + (int)key.Key - (int)ConsoleKey.A);
        return true;
    }

    private static bool IsFilenameCharacter(char ch) =>
        !char.IsControl(ch) && Array.IndexOf(Path.GetInvalidFileNameChars(), ch) < 0;

    private void MoveCursor()
    {
        if (State is not { } quickSearch)
            return;

        var state = _getPanelState(quickSearch.PanelSide);
        if (PanelController.TryFindFirstQuickSearchMatch(
                state,
                quickSearch.SearchText,
                out int itemIndex))
        {
            _panelController.SetCursorTo(state, itemIndex, _visibleRows(quickSearch.PanelSide));
        }
    }

    private PanelSide PanelSideForState(FilePanelState state) =>
        ReferenceEquals(state, _getPanelState(PanelSide.Left))
            ? PanelSide.Left
            : PanelSide.Right;
}

internal enum PanelQuickSearchKeyResult
{
    NotHandled,
    Handled,
    CloseAndContinue,
}
