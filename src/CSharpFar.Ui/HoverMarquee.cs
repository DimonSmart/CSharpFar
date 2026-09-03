using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>A committed hover target whose overflowing text can be animated.</summary>
public sealed record HoverMarqueeRegistration
{
    public HoverMarqueeRegistration(object identity, string text, Rect bounds, int visibleCellWidth)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(text);
        if (visibleCellWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(visibleCellWidth));
        Identity = identity;
        Text = text;
        Bounds = bounds;
        VisibleCellWidth = visibleCellWidth;
    }

    public object Identity { get; }
    public string Text { get; }
    public Rect Bounds { get; }
    public int VisibleCellWidth { get; }
}

/// <summary>
/// UI-thread state for one-at-a-time hover marquees. Its deadlines plug directly
/// into the timed-wake callbacks used by interactive UI hosts.
/// </summary>
public sealed class HoverMarquee
{
    public static TimeSpan HoverDelay { get; } = TimeSpan.FromMilliseconds(600);
    public static TimeSpan StepInterval { get; } = TimeSpan.FromMilliseconds(100);
    public static TimeSpan FinalPause { get; } = TimeSpan.FromMilliseconds(1000);

    private readonly TimeProvider _timeProvider;
    private HoverMarqueeRegistration[] _registrations = [];
    private HoverMarqueeRegistration? _owner;
    private DateTimeOffset? _nextWakeUtc;
    private int _offset;
    private int? _pointerX;
    private int? _pointerY;
    private Phase _phase;

    public HoverMarquee(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public DateTimeOffset? NextWakeUtc => _nextWakeUtc;
    public object? ActiveIdentity => _owner?.Identity;
    public int CellOffset => _offset;

    /// <summary>Replaces the registrations with a successfully committed frame snapshot.</summary>
    public bool SetRegistrations(IEnumerable<HoverMarqueeRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        HoverMarqueeRegistration[] snapshot = registrations.ToArray();
        if (snapshot.Any(static item => item is null))
            throw new ArgumentException("Registrations cannot contain null.", nameof(registrations));

        bool invalidated = false;
        if (_owner is not null)
        {
            HoverMarqueeRegistration? replacement = snapshot.FirstOrDefault(item =>
                Equals(item.Identity, _owner.Identity));
            if (replacement is null || replacement != _owner)
            {
                invalidated = Reset();
                _owner = null;
            }
        }

        _registrations = snapshot;
        return AcquireAtPointer() || invalidated;
    }

    /// <summary>Updates pointer location; pass null to cancel hover immediately.</summary>
    public bool SetPointer(int? x, int? y)
    {
        if (x.HasValue != y.HasValue)
            throw new ArgumentException("Pointer coordinates must both have values or both be null.");
        _pointerX = x;
        _pointerY = y;

        HoverMarqueeRegistration? hovered = FindHovered();
        if (_owner is not null && (hovered is null || !Equals(hovered.Identity, _owner.Identity)))
        {
            bool invalidated = Reset();
            _owner = null;
            return Acquire(hovered) || invalidated;
        }
        return Acquire(hovered);
    }

    /// <summary>Processes a due timed wake and reports whether the UI should invalidate.</summary>
    public bool HandleWake()
    {
        if (_owner is null || _nextWakeUtc is null || _timeProvider.GetUtcNow() < _nextWakeUtc)
            return false;

        int maximum = Math.Max(0, ConsoleTextMetrics.GetCellWidth(_owner.Text) - _owner.VisibleCellWidth);
        if (_phase is Phase.Waiting or Phase.Scrolling)
        {
            _offset = Math.Min(maximum, _offset + 1);
            if (_offset >= maximum)
            {
                _phase = Phase.FinalPause;
                _nextWakeUtc = _timeProvider.GetUtcNow() + FinalPause;
            }
            else
            {
                _phase = Phase.Scrolling;
                _nextWakeUtc = _timeProvider.GetUtcNow() + StepInterval;
            }
            return true;
        }

        _offset = 0;
        _phase = Phase.Completed;
        _nextWakeUtc = null;
        return true;
    }

    public string GetText(HoverMarqueeRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        int offset = _owner is not null && _owner == registration ? _offset : 0;
        return ConsoleTextMetrics.SliceToCells(registration.Text, offset, registration.VisibleCellWidth);
    }

    private bool AcquireAtPointer() => Acquire(FindHovered());

    private bool Acquire(HoverMarqueeRegistration? hovered)
    {
        if (_owner is not null || hovered is null ||
            ConsoleTextMetrics.GetCellWidth(hovered.Text) <= hovered.VisibleCellWidth)
            return false;
        _owner = hovered;
        _offset = 0;
        _phase = Phase.Waiting;
        _nextWakeUtc = _timeProvider.GetUtcNow() + HoverDelay;
        return false;
    }

    private HoverMarqueeRegistration? FindHovered() =>
        _pointerX is int x && _pointerY is int y
            ? _registrations.FirstOrDefault(item => item.Bounds.Contains(x, y))
            : null;

    private bool Reset()
    {
        bool invalidated = _offset != 0;
        _offset = 0;
        _phase = Phase.None;
        _nextWakeUtc = null;
        return invalidated;
    }

    private enum Phase { None, Waiting, Scrolling, FinalPause, Completed }
}
