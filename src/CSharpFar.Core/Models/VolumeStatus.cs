namespace CSharpFar.Core.Models;

public enum VolumeStatus
{
    /// <summary>Availability confirmed (cheaply or already known).</summary>
    Ready,
    /// <summary>
    /// Availability not checked — used for network volumes to avoid blocking the UI.
    /// Not an error: displayed by VolumeKind, selectable by the user.
    /// </summary>
    Unchecked,
    NotReady,
    Disconnected,
    Error,
}
