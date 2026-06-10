using CSharpFar.Core.Models;

namespace CSharpFar.App.Panels;

internal sealed class PanelQuickSearchState
{
    public PanelQuickSearchState(PanelSide panelSide, char firstCharacter)
    {
        PanelSide = panelSide;
        SearchText = Normalize(firstCharacter).ToString();
    }

    public PanelSide PanelSide { get; }

    public string SearchText { get; private set; }

    public void Append(char ch) =>
        SearchText += Normalize(ch);

    public bool RemoveLastCharacter()
    {
        if (SearchText.Length == 0)
            return false;

        SearchText = SearchText[..^1];
        return SearchText.Length > 0;
    }

    private static char Normalize(char ch) =>
        char.ToLowerInvariant(ch);
}
