namespace CSharpFar.Core.Menu;

public sealed class MenuState
{
    public MenuOpenState OpenState { get; set; } = MenuOpenState.Closed;
    public int ActiveTopMenuIndex { get; set; }
    public int ActiveDropdownItemIndex { get; set; }
    public int DropdownFirstVisibleItemIndex { get; set; }
}
