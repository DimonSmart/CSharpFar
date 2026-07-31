using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Small application-scoped factory for ordinary form text fields.</summary>
public sealed class FormFieldFactory
{
    private readonly ITextFieldHistoryProvider _history;

    public FormFieldFactory(ITextFieldHistoryProvider history) =>
        _history = history ?? throw new ArgumentNullException(nameof(history));

    public TextField Text(string id, string initialText = "", TextHistoryId? history = null,
        bool maskInput = false, int? width = null, bool submitOnEnter = false)
    {
        var field = new TextField(id, initialText, maskInput ? null : history is { } key ? _history.Get(key) : null, width, maskInput, submitOnEnter);
        return field;
    }
}

public sealed class TextField
{
    private readonly CommandLineState _buffer = new();
    private readonly TextHistory? _history;

    internal TextField(string id, string initialText, TextHistory? history, int? width, bool maskInput, bool submitOnEnter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        _history = history;
        IsMasked = maskInput;
        Width = width;
        SubmitOnEnter = submitOnEnter;
        if (initialText.Length > 0) _buffer.SetText(initialText);
        Input = new FormTextInputField(
            _buffer,
            history is null ? null : new SingleLineTextHistoryState(history),
            maskInput);
    }

    public string Id { get; }
    public bool IsMasked { get; }
    public int? Width { get; }
    public string Text { get => _buffer.Text; set => _buffer.SetText(value ?? string.Empty); }
    public string TrimmedText => Text.Trim();
    public bool SubmitOnEnter { get; }
    public TextInputRow AsRow() => new(this);
    public LabeledTextInputRow AsLabeledRow(string label, int labelWidth = 22) =>
        new(label, this, labelWidth);
    public void SelectAll() => _buffer.SelectAll();
    internal CommandLineState Buffer => _buffer;
    internal TextHistory? History => _history;
    internal FormTextInputField Input { get; }
    public void AcceptHistory() => _history?.Add(TrimmedText);
}
