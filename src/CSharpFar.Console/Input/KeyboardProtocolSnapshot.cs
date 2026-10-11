namespace CSharpFar.Console.Input;

/// <summary>
/// Protocol-neutral diagnostic state for enhanced terminal keyboard reporting.
/// </summary>
public sealed class KeyboardProtocolSnapshot
{
    internal KeyboardProtocolSnapshot(
        string protocol,
        string supportStatus,
        bool requested,
        int requestedFlags,
        int? confirmedFlags,
        bool isActive,
        string? activeScreen,
        string? fallbackReason,
        int activeFlags = 0)
    {
        Protocol = protocol;
        SupportStatus = supportStatus;
        Requested = requested;
        RequestedFlags = requestedFlags;
        ConfirmedFlags = confirmedFlags;
        IsActive = isActive;
        ActiveScreen = activeScreen;
        FallbackReason = fallbackReason;
        ActiveFlags = activeFlags;
    }

    public string Protocol { get; }

    public string SupportStatus { get; }

    public bool Requested { get; }

    public int RequestedFlags { get; }

    public int? ConfirmedFlags { get; }

    public bool IsActive { get; }

    public string? ActiveScreen { get; }

    public string? FallbackReason { get; }

    public int ActiveFlags { get; }

    public bool ReportsKeyEventTypes => IsActive && (ActiveFlags & 2) != 0;

    public bool CanTrackStandaloneModifiers => IsActive && (ActiveFlags & 27) == 27;

    public static KeyboardProtocolSnapshot NotApplicable { get; } = new(
        protocol: "not applicable",
        supportStatus: "not applicable",
        requested: false,
        requestedFlags: 0,
        confirmedFlags: null,
        isActive: false,
        activeScreen: null,
        fallbackReason: null);
}
