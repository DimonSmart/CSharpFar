namespace CSharpFar.App.Editor;

public sealed class EditorSyntaxLanguage
{
    public EditorSyntaxLanguage(string id, string scopeName, string displayName)
    {
        Id = id;
        ScopeName = scopeName;
        DisplayName = displayName;
    }

    public string Id { get; }
    public string ScopeName { get; }
    public string DisplayName { get; }
}
