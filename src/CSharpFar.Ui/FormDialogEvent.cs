using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

/// <summary>Describes a semantic event raised by an ordinary modal form.</summary>
public readonly record struct FormDialogEvent(
    FormDialogEventKind Kind,
    string? Command = null,
    string? SourceRowId = null,
    ConsoleKey? Key = null,
    string? FocusedRowId = null,
    IFormFocusTarget? SourceTarget = null)
{
    public bool IsHandled => Kind != FormDialogEventKind.NotHandled;
    public bool IsValueChanged => Kind == FormDialogEventKind.ValueChanged;
    public bool IsSubmitted => Kind == FormDialogEventKind.Submitted;
    public bool IsAuxiliary => Kind == FormDialogEventKind.Auxiliary;
    public bool IsCancelled => Kind == FormDialogEventKind.Cancelled;

    /// <summary>Determines whether this is a value-change event from the specified control.</summary>
    public bool IsValueChangedFrom(IFormFocusTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return IsValueChanged && ReferenceEquals(SourceTarget, target);
    }
}

/// <summary>Classifies a semantic event raised by an ordinary modal form.</summary>
public enum FormDialogEventKind
{
    NotHandled,
    Handled,
    ValueChanged,
    Submitted,
    Auxiliary,
    Cancelled,
}
