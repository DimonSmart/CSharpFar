using System.ComponentModel;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Small application-scoped factory for ordinary form text fields.</summary>
public sealed class FormFieldFactory
{
    private readonly ITextFieldHistoryProvider _history;
    private readonly TextFieldDefaults _defaults = new();

    public FormFieldFactory(ITextFieldHistoryProvider history) =>
        _history = history ?? throw new ArgumentNullException(nameof(history));

    private FormFieldFactory(ITextFieldHistoryProvider history, TextFieldDefaults defaults)
    {
        _history = history;
        _defaults = defaults;
    }

    /// <summary>Creates a factory whose fields inherit the supplied form-scoped defaults.</summary>
    public FormFieldFactory WithDefaults(TextFieldDefaults defaults) =>
        new(_history, defaults ?? throw new ArgumentNullException(nameof(defaults)));

    [EditorBrowsable(EditorBrowsableState.Never)]
    public TextField Text(string id, string initialText = "", TextHistoryId? historyId = null,
        bool maskInput = false, int? width = null, bool? submitOnEnter = null)
        => CreateText(id, initialText, historyId, maskInput, width, submitOnEnter);

    /// <summary>Creates an ordinary text field without an application-owned row ID.</summary>
    public TextField Text() => CreateText(null, string.Empty, null, false, null, null);

    /// <summary>Creates an ordinary text field without an application-owned row ID.</summary>
    public TextField Text(TextFieldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return CreateText(null, options.InitialText, options.HistoryId, options.MaskInput, options.Width, options.SubmitOnEnter);
    }

    private TextField CreateText(string? id, string initialText, TextHistoryId? historyId,
        bool maskInput, int? width, bool? submitOnEnter)
    {
        var field = new TextField(
            id,
            initialText,
            maskInput ? null : historyId is { } key ? _history.Get(key) : null,
            width ?? _defaults.Width,
            maskInput,
            submitOnEnter ?? _defaults.SubmitOnEnter);
        return field;
    }
}

/// <summary>Form-scoped defaults for standard text fields.</summary>
public sealed record TextFieldDefaults(int? Width = null, bool SubmitOnEnter = false);

/// <summary>Semantic options for an ID-less standard text field.</summary>
public sealed record TextFieldOptions(
    string InitialText = "",
    TextHistoryId? HistoryId = null,
    bool MaskInput = false,
    int? Width = null,
    bool? SubmitOnEnter = null);

public sealed class TextField : IFormFocusTarget
{
    private readonly CommandLineState _buffer = new();
    private readonly TextHistory? _history;

    internal TextField(string? id, string initialText, TextHistory? history, int? width, bool maskInput, bool submitOnEnter)
    {
        Id = id;
        _history = history;
        IsMasked = maskInput;
        Width = width;
        PreferredWidth = Math.Max(20, ConsoleTextMetrics.GetCellWidth(initialText));
        SubmitOnEnter = submitOnEnter;
        if (initialText.Length > 0) _buffer.SetText(initialText);
        Input = new FormTextInputField(
            _buffer,
            history is null ? null : new SingleLineTextHistoryState(history),
            maskInput);
    }

    public string? Id { get; }
    public bool IsMasked { get; }
    public int? Width { get; }
    internal int PreferredWidth { get; }
    public bool Enabled
    {
        get => Input.Enabled;
        set => Input.Enabled = value;
    }
    public string? DisabledReason
    {
        get => Input.DisabledReason;
        set => Input.DisabledReason = value;
    }
    public string Text { get => _buffer.Text; set => _buffer.SetText(value ?? string.Empty); }
    public string TrimmedText => Text.Trim();
    public bool SubmitOnEnter { get; }
    public void SelectAll() => _buffer.SelectAll();
    internal CommandLineState Buffer => _buffer;
    internal TextHistory? History => _history;
    internal FormTextInputField Input { get; }
    public void AcceptHistory() => _history?.Add(TrimmedText);
}
