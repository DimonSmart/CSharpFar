namespace CSharpFar.Core.Models;

public sealed record FileAttributeDescriptor(
    FileAttributeId Id,
    string Label,
    char? HotKey,
    bool IsVisible,
    bool IsEditable,
    string? DisabledReason = null);
