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
    DialogButtonRole Role = DialogButtonRole.Submit);
