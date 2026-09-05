namespace CSharpFar.Ui;

internal static class ScrollStateCalculator
{
    public static int ClampFirstVisibleIndex(int firstVisibleIndex, int totalItems, int viewportItems)
    {
        if (totalItems <= 0 || viewportItems <= 0) return 0;
        return Math.Clamp(firstVisibleIndex, 0, Math.Max(0, totalItems - viewportItems));
    }

    public static int EnsureIndexVisible(int itemIndex, int firstVisibleIndex, int viewportItems)
    {
        if (itemIndex < 0 || viewportItems <= 0) return 0;
        if (itemIndex < firstVisibleIndex) return itemIndex;
        return itemIndex >= firstVisibleIndex + viewportItems
            ? itemIndex - viewportItems + 1
            : Math.Max(0, firstVisibleIndex);
    }

    public static void NormalizeSelection(int totalItems, int viewportItems, ref int selectedIndex, ref int firstVisibleIndex)
    {
        if (totalItems <= 0) { selectedIndex = 0; firstVisibleIndex = 0; return; }
        selectedIndex = Math.Clamp(selectedIndex, 0, totalItems - 1);
        firstVisibleIndex = ClampFirstVisibleIndex(firstVisibleIndex, totalItems, viewportItems);
        firstVisibleIndex = EnsureIndexVisible(selectedIndex, firstVisibleIndex, viewportItems);
        firstVisibleIndex = ClampFirstVisibleIndex(firstVisibleIndex, totalItems, viewportItems);
    }

    public static void MoveSelection(int delta, int totalItems, int viewportItems, ref int selectedIndex, ref int firstVisibleIndex)
    {
        if (totalItems <= 0) return;
        selectedIndex = Math.Clamp(selectedIndex + delta, 0, totalItems - 1);
        NormalizeSelection(totalItems, viewportItems, ref selectedIndex, ref firstVisibleIndex);
    }
}
