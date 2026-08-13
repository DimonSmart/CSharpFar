namespace CSharpFar.Ui;

internal readonly record struct FramePresentationMeasurement(
    TimeSpan FrameTime,
    TimeSpan PresentTime,
    int DirtyCells,
    int DirtyRows,
    int OutputCalls,
    int ViewportQueryCalls,
    int TransmittedCells,
    int TransmittedCharacters,
    int TransmittedBytes,
    long AllocatedBytes);

internal sealed class FramePresentationMetrics
{
    private const int MaximumSamples = 4_096;
    private readonly List<FramePresentationMeasurement> _frames = [];

    public IReadOnlyList<FramePresentationMeasurement> Frames => _frames;

    public FramePresentationMeasurement Last => _frames.Count == 0 ? default : _frames[^1];

    public void Add(FramePresentationMeasurement measurement)
    {
        if (_frames.Count == MaximumSamples)
            _frames.RemoveAt(0);

        _frames.Add(measurement);
    }

    public FramePresentationReport CreateReport()
    {
        if (_frames.Count == 0)
            return default;

        return new FramePresentationReport(
            Frames: _frames.Count,
            PresentP50: Percentile(_frames.Select(frame => frame.PresentTime), 50),
            PresentP95: Percentile(_frames.Select(frame => frame.PresentTime), 95),
            PresentP99: Percentile(_frames.Select(frame => frame.PresentTime), 99),
            FrameP50: Percentile(_frames.Select(frame => frame.FrameTime), 50),
            FrameP95: Percentile(_frames.Select(frame => frame.FrameTime), 95),
            FrameP99: Percentile(_frames.Select(frame => frame.FrameTime), 99),
            OutputCallsPerFrame: _frames.Average(frame => frame.OutputCalls),
            AllocatedBytesPerFrame: _frames.Average(frame => frame.AllocatedBytes));
    }

    private static TimeSpan Percentile(IEnumerable<TimeSpan> values, int percentile)
    {
        var ordered = values.Order().ToArray();
        int index = (int)Math.Ceiling(percentile / 100d * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }
}

internal readonly record struct FramePresentationReport(
    int Frames,
    TimeSpan PresentP50,
    TimeSpan PresentP95,
    TimeSpan PresentP99,
    TimeSpan FrameP50,
    TimeSpan FrameP95,
    TimeSpan FrameP99,
    double OutputCallsPerFrame,
    double AllocatedBytesPerFrame);
