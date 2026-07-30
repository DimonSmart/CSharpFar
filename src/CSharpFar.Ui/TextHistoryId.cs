namespace CSharpFar.Ui;

public readonly record struct TextHistoryId
{
    public TextHistoryId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public interface ITextFieldHistoryProvider
{
    TextHistory Get(TextHistoryId id);
}

/// <summary>Stable persistent history identifiers used by application dialogs.</summary>
public static class TextHistoryIds
{
    public static readonly TextHistoryId SearchMask = new("SearchDialog.Mask");
    public static readonly TextHistoryId SearchText = new("SearchDialog.Text");
    public static readonly TextHistoryId SearchParallelism = new("SearchDialog.Parallelism");
    public static readonly TextHistoryId CreateFolderName = new("CreateFolderDialog.FolderName");
    public static readonly TextHistoryId ViewerFindPattern = new("Viewer.Find.Pattern");
    public static readonly TextHistoryId EditorFindPattern = new("Editor.Find.Pattern");
    public static readonly TextHistoryId FtpHost = new("FtpConnectionDialog.Host");
    public static readonly TextHistoryId SftpHost = new("SftpConnectionDialog.Host");
}
