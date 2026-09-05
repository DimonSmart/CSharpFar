namespace CSharpFar.Ui;

internal static class ScrollStateCalculator
{
    public static int ClampFirstVisibleIndex(int firstVisibleIndex, int totalItems, int viewportItems)
    {
        if (totalItems <= 0 || viewportItems <= 0) return 0;
        return Math.Clamp(firstVisibleIndex, 0, Math.Max(0, totalItems - viewportItems));
    }

    public static int EnsureIndexVisible(int index, int firstVisibleIndex, int totalItems, int viewportItems)
    {
        if (totalItems <= 0 || viewportItems <= 0) return 0;
        int selected = Math.Clamp(index, 0, totalItems - 1);
        int first = ClampFirstVisibleIndex(firstVisibleIndex, totalItems, viewportItems);
        if (selected < first) first = selected;
        else if (selected >= first + viewportItems) first = selected - viewportItems + 1;
        return ClampFirstVisibleIndex(first, totalItems, viewportItems);
    }

    public static ScrollState CreateScrollState(int firstVisibleIndex, int totalItems, int viewportItems)
    {
        if (totalItems <= 0 || viewportItems <= 0)
            return new ScrollState(0, 0, Math.Max(0, viewportItems));
        return new ScrollState(
            ClampFirstVisibleIndex(firstVisibleIndex, totalItems, viewportItems),
            totalItems,
            viewportItems);
    }
}
