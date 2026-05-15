namespace CSharpFar.Ui;

public sealed record DialogButton(
    string Id,
    string Text,
    char HotKey,
    bool IsDefault = false);
