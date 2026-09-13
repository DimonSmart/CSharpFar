using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public static class TransientPopupPlacementExtensions
{
    /// <summary>Returns the presentation bounds implied by this semantic placement policy, if the selection popup fits.</summary>
    public static Rect? CalculateSelectionBounds(
        this TransientPopupPlacement placement,
        ConsoleSize size,
        int itemCount)
    {
        ArgumentNullException.ThrowIfNull(placement);
        TransientPopupLayout layout = placement.CalculateSelection(size, itemCount);
        return layout.IsVisible ? layout.PopupBounds : null;
    }
}
