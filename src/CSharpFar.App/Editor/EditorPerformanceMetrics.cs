using System.Diagnostics;

namespace CSharpFar.App.Editor;

/// <summary>Opt-in diagnostics used by the editor performance harness.</summary>
internal sealed class EditorPerformanceMetrics
{
    private readonly List<EditorPerformanceFrameMeasurement> _frames = [];

    public IReadOnlyList<EditorPerformanceFrameMeasurement> Frames => _frames;
    public TimeSpan InputHandling { get; private set; }
    public TimeSpan RenderClassification { get; private set; }
    public TimeSpan DrawTextLine { get; private set; }

    public void AddInput(TimeSpan elapsed) => InputHandling += elapsed;
    public void AddRenderClassification(TimeSpan elapsed) => RenderClassification += elapsed;
    public void AddDrawTextLine(TimeSpan elapsed) => DrawTextLine += elapsed;
    public void AddFrame(EditorPerformanceFrameMeasurement measurement) => _frames.Add(measurement);

    public static long Timestamp() => Stopwatch.GetTimestamp();
    public static TimeSpan Elapsed(long start) => Stopwatch.GetElapsedTime(start);
}

internal readonly record struct EditorPerformanceFrameMeasurement(
    bool IsFull,
    TimeSpan Total,
    TimeSpan Viewport,
    TimeSpan Syntax,
    TimeSpan DrawContent,
    TimeSpan DrawTextLines);

internal sealed record FileEditorPerformanceOptions(
    EditorPerformanceMetrics Metrics);
