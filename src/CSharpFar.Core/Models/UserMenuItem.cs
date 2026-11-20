using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CSharpFar.Core.Models;

public sealed class UserMenuItem
{
    public required string Title { get; init; }
    public string? Command { get; init; }
    public PlatformKind? Platform { get; init; }

    // Preserve the public SDK accessor for already-compiled plugins, but do not
    // retain, serialize or use legacy IDs when merging user menus.
    [Obsolete("User menu IDs are ignored.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [JsonIgnore]
    public string? Id
    {
        get => null;
        init { }
    }
}
