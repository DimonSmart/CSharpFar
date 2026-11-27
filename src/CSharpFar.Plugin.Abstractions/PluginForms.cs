namespace CSharpFar.Plugin.Abstractions;

/// <summary>Semantic rows rendered by the host; no terminal or UI-library dependencies.</summary>
public abstract class PluginFormRow
{
    private protected PluginFormRow(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; }
    public bool Visible { get; set; } = true;
}

public abstract class PluginFormField(string id, string label) : PluginFormRow(id)
{
    public string Label { get; } = label;
    public bool Enabled { get; set; } = true;
}

/// <summary>A single-line input. Masked fields never use host input history.</summary>
public sealed class PluginFormText(string id, string label, string value = "") : PluginFormField(id, label)
{
    public string Value { get; set; } = value;
    public bool Masked { get; init; }
    public IReadOnlyList<PluginFormButton> Actions { get; init; } = [];
}

public sealed class PluginFormCheckBox(string id, string label, bool value = false) : PluginFormField(id, label)
{
    public bool Value { get; set; } = value;
}

/// <summary>Single selection from a fixed set of distinct, nonempty options.</summary>
public sealed class PluginFormSelect(string id, string label, IReadOnlyList<string> options, string value)
    : PluginFormField(id, label)
{
    public IReadOnlyList<string> Options { get; } = Array.AsReadOnly(options.ToArray());
    public string Value { get; set; } = value;
}

public sealed class PluginFormLabel(string id, string text) : PluginFormRow(id)
{
    public string Text { get; set; } = text;
}

public sealed class PluginFormSeparator(string id) : PluginFormRow(id);

public enum PluginFormButtonRole { Submit, Auxiliary, Cancel }

public sealed record PluginFormButton(string Id, string Text, char HotKey,
    PluginFormButtonRole Role = PluginFormButtonRole.Submit, bool IsDefault = false, bool Enabled = true);

public enum PluginFormEventKind { ValueChanged, Action, Submit }

/// <summary>Identifies the changed field or invoked button. Values are available on the draft fields.</summary>
public sealed record PluginFormEvent(PluginFormEventKind Kind, string Id);

/// <summary>Return a validation error to keep the form open; optionally focus a visible, enabled field.</summary>
public sealed record PluginFormFeedback(string Error, string? FocusFieldId = null);

/// <summary>
/// A modal form with a fixed set of rows. Callbacks run synchronously on the terminal thread
/// after input is copied into the draft. They may change values, visibility, enabled state,
/// label text and status. Null feedback accepts a submit or continues an auxiliary action.
/// Rows and buttons must not be added or removed during the call; use Visible for conditional rows.
/// Draft values can change even on cancellation; commit application settings only on success.
/// </summary>
public sealed class PluginForm(string title, IReadOnlyList<PluginFormRow> rows)
{
    public string Title { get; } = title;
    public IReadOnlyList<PluginFormRow> Rows { get; } = Array.AsReadOnly(rows.ToArray());
    public int? PreferredWidth { get; init; }
    public int? PreferredHeight { get; init; }
    public string? InitialFocusFieldId { get; init; }
    public string Status { get; set; } = "";
    public IReadOnlyList<PluginFormButton> Buttons { get; init; } =
    [
        new("ok", "OK", 'O', IsDefault: true),
        new("cancel", "Cancel", 'C', PluginFormButtonRole.Cancel)
    ];
    public Func<PluginFormEvent, PluginFormFeedback?>? OnEvent { get; init; }
}
