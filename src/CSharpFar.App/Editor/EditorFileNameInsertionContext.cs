namespace CSharpFar.App.Editor;

public sealed record EditorFileNameInsertionContext(
    string? ActivePanelItemName,
    string? ActivePanelItemPath,
    string? PassivePanelItemName,
    string? PassivePanelItemPath);
