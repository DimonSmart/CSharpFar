namespace CSharpFar.Core.Models;

public static class ScrollStateCalculator
{
    public static int ClampFirstVisibleIndex(int firstVisibleIndex, int totalItems, int viewportItems)
    {
        if (totalItems <= 0 || viewportItems <= 0)
            return 0;

        int maxFirstVisibleIndex = Math.Max(0, totalItems - viewportItems);
        return Math.Clamp(firstVisibleIndex, 0, maxFirstVisibleIndex);
    }

    public static int EnsureIndexVisible(int itemIndex, int firstVisibleIndex, int viewportItems)
    {
        if (itemIndex < 0 || viewportItems <= 0)
            return 0;

        if (itemIndex < firstVisibleIndex)
            return itemIndex;

        if (itemIndex >= firstVisibleIndex + viewportItems)
            return itemIndex - viewportItems + 1;

        return Math.Max(0, firstVisibleIndex);
    }

    public static void NormalizeSelection(
        int totalItems,
        int viewportItems,
        ref int selectedIndex,
        ref int firstVisibleIndex)
    {
        if (totalItems <= 0)
        {
            selectedIndex = 0;
            firstVisibleIndex = 0;
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, totalItems - 1);
        firstVisibleIndex = ClampFirstVisibleIndex(firstVisibleIndex, totalItems, viewportItems);
        firstVisibleIndex = EnsureIndexVisible(selectedIndex, firstVisibleIndex, viewportItems);
        firstVisibleIndex = ClampFirstVisibleIndex(firstVisibleIndex, totalItems, viewportItems);
    }

    public static void MoveSelection(
        int delta,
        int totalItems,
        int viewportItems,
        ref int selectedIndex,
        ref int firstVisibleIndex)
    {
        if (totalItems <= 0)
            return;

        selectedIndex = Math.Clamp(selectedIndex + delta, 0, totalItems - 1);
        NormalizeSelection(totalItems, viewportItems, ref selectedIndex, ref firstVisibleIndex);
    }
}
