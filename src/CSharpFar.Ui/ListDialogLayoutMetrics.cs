using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

internal static class ListDialogLayoutMetrics
{
    public const int HorizontalContentPadding = 1;
    public const int VerticalContentPadding = 1;
    public const int HorizontalContentChrome = HorizontalContentPadding * 2;
    public const int VerticalContentChrome = VerticalContentPadding * 2;

    public static Rect InsetContent(Rect frameContentBounds) =>
        UiLayout.Inset(
            frameContentBounds,
            HorizontalContentPadding,
            VerticalContentPadding,
            HorizontalContentPadding,
            VerticalContentPadding);
}
