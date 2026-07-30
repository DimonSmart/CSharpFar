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
        if (initialText.Length > 0) _buffer.SetText(initialText);
        Row = new TextInputRow(_buffer, _history, width: width, maskInput: maskInput) { Id = id, SubmitOnEnter = submitOnEnter };
    }

    public string Id { get; }
    public string Text { get => _buffer.Text; set => _buffer.SetText(value ?? string.Empty); }
    public string TrimmedText => Text.Trim();
    public bool SubmitOnEnter => Row.SubmitOnEnter;
    public TextInputRow Row { get; }
    public void AcceptHistory() => _history?.Add(TrimmedText);
}
