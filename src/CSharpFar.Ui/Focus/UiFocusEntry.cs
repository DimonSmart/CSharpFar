namespace CSharpFar.Ui;

public sealed record UiFocusEntry(
    UiTargetId Target,
    int TabOrder,
    bool IsEnabled = true,
    UiCursorPlacement? Cursor = null);
