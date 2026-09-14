namespace CSharpFar.Ui;

/// <summary>One semantic page in a reusable settings dialog.</summary>
public sealed class SettingsPage
{
    public SettingsPage(
        string id,
        string title,
        IReadOnlyList<FormRow> rows,
        Func<FormSubmitResult<bool>>? validate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(rows);

        Id = id;
        Title = title;
        Rows = Array.AsReadOnly(rows.ToArray());
        Validate = validate;
    }

    public string Id { get; }
    public string Title { get; }
    public IReadOnlyList<FormRow> Rows { get; }
    public Func<FormSubmitResult<bool>>? Validate { get; }
}
