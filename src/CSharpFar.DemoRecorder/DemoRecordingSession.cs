using System.Text;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.DemoRecorder;

internal sealed class DemoRecordingSession
{
    private const int IntermediateTextHoldMs = 40;

    private readonly RecordingConsoleDriver _driver;
    private readonly SnapshotRasterizer _rasterizer;
    private readonly FrameSequenceWriter _frameWriter;
    private readonly DemoScenario _scenario;
    private readonly string _outputDirectory;
    private readonly string _defaultScreenshotPath;
    private readonly Queue<QueuedInput> _queuedInputs = new();
    private int _stepIndex;
    private int _pendingHoldMs;
    private bool _capturedInitialFrame;
    private bool _completed;

    public DemoRecordingSession(
        RecordingConsoleDriver driver,
        SnapshotRasterizer rasterizer,
        FrameSequenceWriter frameWriter,
        DemoScenario scenario,
        string outputDirectory,
        string defaultScreenshotPath)
    {
        _driver = driver;
        _rasterizer = rasterizer;
        _frameWriter = frameWriter;
        _scenario = scenario;
        _outputDirectory = outputDirectory;
        _defaultScreenshotPath = defaultScreenshotPath;
        _pendingHoldMs = scenario.DefaultHoldMs;
    }

    public void OnApplicationReady()
    {
        ScreenSnapshot snapshot = _driver.CaptureViewport();
        if (_capturedInitialFrame)
            _frameWriter.Append(snapshot, _driver.CursorX, _driver.CursorY, _driver.CursorVisible, _pendingHoldMs, _rasterizer, _scenario.FramesPerSecond);

        _capturedInitialFrame = true;

        if (TryDispatchQueuedInput())
            return;

        while (_stepIndex < _scenario.Steps.Count)
        {
            DemoScenarioStep step = _scenario.Steps[_stepIndex];
            _stepIndex++;

            switch (step)
            {
                case DemoWaitStep wait:
                    _pendingHoldMs += wait.DurationMs;
                    continue;

                case DemoExpectTextStep expect:
                    AssertContainsText(snapshot, expect.Text);
                    continue;

                case DemoScreenshotStep screenshot:
                    {
                        string path = Path.Combine(_outputDirectory, screenshot.FileName);
                        _rasterizer.SavePng(snapshot, _driver.CursorX, _driver.CursorY, _driver.CursorVisible, path);
                        if (!File.Exists(_defaultScreenshotPath))
                            File.Copy(path, _defaultScreenshotPath, overwrite: true);
                        continue;
                    }

                case DemoTextStep text:
                    EnqueueText(text);
                    if (TryDispatchQueuedInput())
                        return;
                    continue;

                case DemoKeyStep key:
                    _queuedInputs.Enqueue(new QueuedInput(key.Input, key.HoldMs));
                    if (TryDispatchQueuedInput())
                        return;
                    continue;

                default:
                    throw new InvalidOperationException($"Unsupported scenario step '{step.GetType().Name}'.");
            }
        }

        _completed = true;
    }

    public void EnsureCompleted()
    {
        if (!_completed)
            throw new InvalidOperationException($"Scenario '{_scenario.Name}' ended before all recorder steps completed.");

        if (!_capturedInitialFrame)
            throw new InvalidOperationException("The recorder did not observe any committed application frame.");
    }

    private void EnqueueText(DemoTextStep text)
    {
        if (text.Text.Length == 0)
        {
            _pendingHoldMs = text.HoldMs;
            return;
        }

        for (int index = 0; index < text.Text.Length; index++)
        {
            char ch = text.Text[index];
            int holdMs = index == text.Text.Length - 1 ? text.HoldMs : IntermediateTextHoldMs;
            _queuedInputs.Enqueue(new QueuedInput(
                new KeyConsoleInputEvent(new ConsoleKeyInfo(ch, ConsoleKey.None, false, false, false)),
                holdMs));
        }
    }

    private bool TryDispatchQueuedInput()
    {
        if (!_queuedInputs.TryDequeue(out QueuedInput queued))
            return false;

        _driver.EnqueueInput(queued.Input);
        _pendingHoldMs = queued.HoldMs;
        _completed = _queuedInputs.Count == 0 && _stepIndex >= _scenario.Steps.Count;
        return true;
    }

    private static void AssertContainsText(ScreenSnapshot snapshot, string text)
    {
        var builder = new StringBuilder();
        for (int row = 0; row < snapshot.Region.Height; row++)
        {
            for (int col = 0; col < snapshot.Region.Width; col++)
                builder.Append(snapshot.Cells[row, col].Character);
            builder.AppendLine();
        }

        if (!builder.ToString().Contains(text, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected committed frame to contain '{text}'.");
    }

    private readonly record struct QueuedInput(ConsoleInputEvent Input, int HoldMs);
}
