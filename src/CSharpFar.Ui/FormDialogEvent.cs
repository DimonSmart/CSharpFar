using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

/// <summary>Describes a semantic event raised by an ordinary modal form.</summary>
public readonly record struct FormDialogEvent(
    FormDialogEventKind Kind,
    string? Command = null,
    string? SourceRowId = null,
    ConsoleKey? Key = null,
    string? FocusedRowId = null)
{
    public bool IsHandled => Kind != FormDialogEventKind.NotHandled;
    public bool IsValueChanged => Kind == FormDialogEventKind.ValueChanged;
    public bool IsSubmitted => Kind == FormDialogEventKind.Submitted;
    public bool IsCancelled => Kind == FormDialogEventKind.Cancelled;
}

/// <summary>Classifies a semantic event raised by an ordinary modal form.</summary>
public enum FormDialogEventKind
{
    NotHandled,
    Handled,
    ValueChanged,
    Submitted,
    Cancelled,
}
