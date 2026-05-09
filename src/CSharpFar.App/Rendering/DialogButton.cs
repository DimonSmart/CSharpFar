namespace CSharpFar.App.Rendering;

internal sealed record DialogButton(
    string Id,
    string Text,
    char HotKey,
    bool IsDefault = false);
