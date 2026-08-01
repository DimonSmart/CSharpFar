namespace CSharpFar.Ui;

public enum DialogButtonRole
{
    Submit,
    Cancel,
}

public sealed record DialogButton(
    string Id,
    string Text,
    char HotKey,
    bool IsDefault = false,
    bool IsEnabled = true,
    DialogButtonRole Role = DialogButtonRole.Submit)
{
    public static DialogButton Default(string id, string text, char hotKey) =>
        new(id, text, hotKey, IsDefault: true);

    public static DialogButton Submit(string id, string text, char hotKey) =>
        new(id, text, hotKey);

    public static DialogButton Action(string id, string text, char hotKey) =>
        new(id, text, hotKey);

    public static DialogButton Cancel(string text = "Cancel", char hotKey = 'C', string id = "cancel") =>
        new(id, text, hotKey, Role: DialogButtonRole.Cancel);
}
