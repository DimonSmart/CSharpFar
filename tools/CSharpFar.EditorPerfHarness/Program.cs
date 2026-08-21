using System.Diagnostics;
using System.Reflection;
using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.EditorPerfHarness;

internal static class Program
{
    private const int Repetitions = 3;

    private static int Main(string[] args)
    {
        string artifactDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "editor-perf"));
        Directory.CreateDirectory(artifactDirectory);
        var fixtures = new FixtureGenerator(artifactDirectory);
        fixtures.CreateAll();

        var runs = new List<RunResult>();
        string normal = fixtures.PathFor(5000, FixtureLength.Normal);
        foreach (bool syntax in new[] { false, true })
        {
            string suffix = syntax ? " syntax-on" : " syntax-off";
            runs.AddRange(Repeat("cursor-only" + suffix, normal, 120, 40, syntax, ScreenPresentationMode.Current, cursorOnly: true));
            runs.AddRange(Repeat("scrolling" + suffix, normal, 120, 40, syntax, ScreenPresentationMode.Current, cursorOnly: false));
        }

        if (args.Contains("--baseline-only", StringComparer.Ordinal))
        {
            string baselineOutput = Path.Combine(artifactDirectory, "baseline-report.md");
            File.WriteAllText(baselineOutput, Report.Create(runs, fixtures, artifactDirectory));
            System.Console.WriteLine(baselineOutput);
            return 0;
        }

        foreach (int lines in new[] { 100, 1000, 5000, 20000 })
            runs.AddRange(Repeat($"scrolling-lines-{lines}", fixtures.PathFor(lines, FixtureLength.Normal), 120, 40, false, ScreenPresentationMode.Current, false));
        foreach ((int width, int height) in new[] { (80, 25), (120, 40), (180, 50) })
            runs.AddRange(Repeat($"scrolling-viewport-{width}x{height}", normal, width, height, false, ScreenPresentationMode.Current, false));
        foreach (FixtureLength length in new[] { FixtureLength.Short, FixtureLength.Normal, FixtureLength.Long })
            runs.AddRange(Repeat($"scrolling-{length.ToString().ToLowerInvariant()}-lines", fixtures.PathFor(5000, length), 120, 40, false, ScreenPresentationMode.Current, false));
        foreach (ScreenPresentationMode mode in Enum.GetValues<ScreenPresentationMode>())
            runs.AddRange(Repeat($"scrolling-presentation-{mode}", normal, 120, 40, false, mode, false));

        string report = Report.Create(runs, fixtures, artifactDirectory);
        string output = Path.Combine(artifactDirectory, "report.md");
        File.WriteAllText(output, report);
        System.Console.WriteLine(output);
        System.Console.WriteLine(Report.Summary(runs));
        return 0;
    }

    private static IEnumerable<RunResult> Repeat(string name, string filePath, int width, int height, bool syntax, ScreenPresentationMode mode, bool cursorOnly)
    {
        for (int repetition = 0; repetition < Repetitions; repetition++)
        {
            System.Console.Error.WriteLine($"{name} ({repetition + 1}/{Repetitions})");
            yield return Execute(name, filePath, width, height, syntax, mode, cursorOnly, repetition);
        }
    }

    private static RunResult Execute(string name, string filePath, int width, int height, bool syntax, ScreenPresentationMode mode, bool cursorOnly, int repetition)
    {
        var driver = new FakeConsoleDriver(width, height)
        {
            Capabilities = ConsoleFrameWriteCapabilities.WindowsCells | ConsoleFrameWriteCapabilities.VirtualTerminalCells,
        };
        var renderer = new ScreenRenderer(driver, mode);
        var composition = new UiCompositionHost(renderer);
        composition.SetRootSurface(new ScreenRendererSurface(renderer, _ => { }));
        var modalDialogs = new ModalDialogHost(composition);
        var surfaces = new InteractiveSurfaceHost(composition);
        var fields = new FormFieldFactory(new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore()));
        var settings = new AppSettings.EditorSettings { SyntaxHighlightingEnabled = syntax };
        var metrics = new EditorPerformanceMetrics();
        var editor = new FileEditor(
            surfaces,
            modalDialogs,
            new DialogService(modalDialogs, fields),
            PaletteRegistry.Default,
            settings,
            null,
            fields,
            null,
            syntax ? new TextMateEditorSyntaxHighlighter() : new NoSyntaxHighlighter(),
            performanceOptions: new FileEditorPerformanceOptions(metrics));

        int inputCount = cursorOnly ? 1_000 : 1_040;
        if (cursorOnly)
        {
            for (int i = 0; i < inputCount; i++)
                driver.EnqueueKey(new ConsoleKeyInfo('\0', i % 2 == 0 ? ConsoleKey.DownArrow : ConsoleKey.UpArrow, false, false, false));
        }
        else
        {
            for (int i = 0; i < inputCount; i++)
                driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        }
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, false, false, false));

        long allocationStart = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        editor.Show(filePath);
        stopwatch.Stop();
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocationStart;

        IReadOnlyList<EditorPerformanceFrameMeasurement> frames = cursorOnly
            ? metrics.Frames.Where(frame => !frame.IsFull).TakeLast(1_000).ToArray()
            : metrics.Frames.Where(frame => frame.IsFull).Take(1_000).ToArray();
        IReadOnlyList<FramePresentationMeasurement> presented = renderer.PresentationMetrics.Frames
            .TakeLast(frames.Count)
            .ToArray();
        return new RunResult(name, repetition, filePath, width, height, syntax, mode, cursorOnly, stopwatch.Elapsed, allocated, frames, presented, metrics);
    }

    private sealed class NoSyntaxHighlighter : IEditorSyntaxHighlighter
    {
        public EditorSyntaxHighlightResult Highlight(EditorSyntaxHighlightRequest request) => EditorSyntaxHighlightResult.Disabled("Syn:off");
    }
}

internal enum FixtureLength { Short, Normal, Long }

internal sealed class FixtureGenerator(string directory)
{
    public string PathFor(int lines, FixtureLength length) => Path.Combine(directory, $"{lines}-{length.ToString().ToLowerInvariant()}-lines.cs");

    public void CreateAll()
    {
        foreach (int lines in new[] { 100, 1000, 5000, 20000 })
            foreach (FixtureLength length in Enum.GetValues<FixtureLength>())
                Create(lines, length);
    }

    private void Create(int lines, FixtureLength length)
    {
        var random = new Random(0x5EED + lines * 31 + (int)length);
        using var writer = new StreamWriter(PathFor(lines, length), append: false, new UTF8Encoding(false));
        for (int line = 1; line <= lines; line++)
        {
            int min = length == FixtureLength.Short ? 20 : length == FixtureLength.Normal ? 20 : 300;
            int max = length == FixtureLength.Short ? 60 : length == FixtureLength.Normal ? 160 : 1000;
            var builder = new StringBuilder($"{line:D6} ");
            while (builder.Length < random.Next(min, max + 1))
            {
                builder.Append(Word(random));
                builder.Append(random.Next(9) == 0 ? "\t" : " ");
            }
            writer.WriteLine(builder);
        }
    }

    private static string Word(Random random) => new[] { "public", "static", "void", "logger", "request", "alpha", "delta", "foxtrot", "event", "cache", "result", "warning", "trace" }[random.Next(13)];
}

internal sealed record RunResult(string Name, int Repetition, string FilePath, int Width, int Height, bool Syntax, ScreenPresentationMode Mode, bool CursorOnly, TimeSpan Elapsed, long Allocated, IReadOnlyList<EditorPerformanceFrameMeasurement> Frames, IReadOnlyList<FramePresentationMeasurement> Presentation, EditorPerformanceMetrics Metrics);

internal static class Report
{
    public static string Create(IReadOnlyList<RunResult> runs, FixtureGenerator fixtures, string artifactDirectory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# CSharpFar editor performance investigation");
        sb.AppendLine();
        sb.AppendLine("## Environment");
        sb.AppendLine();
        sb.AppendLine($"- OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($"- Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"- CPU: {Environment.ProcessorCount} logical processors; {Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown"}");
        sb.AppendLine("- Configuration: Release; deterministic in-memory `FakeConsoleDriver`; full editor/session/composition/ScreenRenderer pipeline.");
        sb.AppendLine("- Fixtures: generated with fixed seed into `artifacts/editor-perf`; ASCII plus spaces/tabs; no fixture is committed.");
        sb.AppendLine();
        sb.AppendLine("## Results");
        sb.AppendLine();
        sb.AppendLine("| Scenario | ms/frame p50 | p95 | p99 | Alloc/frame | Full/partial | Dirty cells | Output calls | Transmitted cells |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var group in runs.GroupBy(run => run.Name))
        {
            var data = Aggregate(group.ToArray());
            sb.AppendLine($"| {group.Key} | {data.P50:F3} | {data.P95:F3} | {data.P99:F3} | {data.Alloc:F0} B | {data.Full:F0}/{data.Partial:F0} | {data.Dirty:F0} | {data.OutputCalls:F2} | {data.Transmitted:F0} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Breakdown (baseline scrolling, syntax off)");
        sb.AppendLine();
        var baseline = Aggregate(runs.Where(run => run.Name == "scrolling syntax-off").ToArray());
        sb.AppendLine("| Component | ms/frame | % of editor render |");
        sb.AppendLine("| --- | ---: | ---: |");
        AddBreakdown(sb, "Input/session", baseline.Input, baseline.Render);
        AddBreakdown(sb, "Render classification", baseline.Classification, baseline.Render);
        AddBreakdown(sb, "Viewport", baseline.Viewport, baseline.Render);
        AddBreakdown(sb, "Syntax", baseline.SyntaxTime, baseline.Render);
        AddBreakdown(sb, "DrawContent", baseline.Draw, baseline.Render);
        AddBreakdown(sb, "DrawTextLine", baseline.Lines, baseline.Render);
        AddBreakdown(sb, "ScreenRenderer presentation", baseline.Present, baseline.Render);
        sb.AppendLine();
        sb.AppendLine("## Conclusions");
        sb.AppendLine();
        sb.AppendLine("The benchmark compares full end-to-end frames. `cursor-only` uses alternating Up/Down inside the viewport; `scrolling` uses 1,040 Down inputs and reports the first 1,000 full frames.");
        sb.AppendLine("Visible editor lines use the production sequential Unicode renderer; the benchmark keeps the complete editor, composition, and presentation path intact.");
        sb.AppendLine();
        sb.AppendLine("## Raw-data note");
        sb.AppendLine();
        sb.AppendLine("Each row is the aggregate of three independent runs. Percentiles are calculated over all selected frames, excluding opening and closing frames.");
        return sb.ToString();
    }

    public static string Summary(IReadOnlyList<RunResult> runs)
    {
        var baseline = Aggregate(runs.Where(run => run.Name == "scrolling syntax-off").ToArray());
        return $"scrolling p50={baseline.P50:F3}ms; p95={baseline.P95:F3}ms";
    }

    private static void AddBreakdown(StringBuilder sb, string component, double value, double total) =>
        sb.AppendLine($"| {component} | {value:F3} | {(total == 0 ? 0 : value / total * 100):F1}% |");

    private static AggregateResult Aggregate(IReadOnlyList<RunResult> runs)
    {
        var frames = runs.SelectMany(run => run.Frames).ToArray();
        var present = runs.SelectMany(run => run.Presentation).ToArray();
        return new AggregateResult(
            Percentile(frames.Select(frame => frame.Total.TotalMilliseconds), 50), Percentile(frames.Select(frame => frame.Total.TotalMilliseconds), 95), Percentile(frames.Select(frame => frame.Total.TotalMilliseconds), 99),
            frames.Average(frame => frame.IsFull ? 1 : 0), frames.Average(frame => frame.IsFull ? 0 : 1),
            frames.Average(frame => frame.Total.TotalMilliseconds), frames.Average(frame => frame.Viewport.TotalMilliseconds), frames.Average(frame => frame.Syntax.TotalMilliseconds), frames.Average(frame => frame.DrawContent.TotalMilliseconds), frames.Average(frame => frame.DrawTextLines.TotalMilliseconds),
            runs.Average(run => run.Metrics.InputHandling.TotalMilliseconds / Math.Max(1, run.Frames.Count)), runs.Average(run => run.Metrics.RenderClassification.TotalMilliseconds / Math.Max(1, run.Frames.Count)),
            present.DefaultIfEmpty().Average(frame => frame.PresentTime.TotalMilliseconds), present.DefaultIfEmpty().Average(frame => frame.AllocatedBytes), present.DefaultIfEmpty().Average(frame => frame.DirtyCells), present.DefaultIfEmpty().Average(frame => frame.OutputCalls), present.DefaultIfEmpty().Average(frame => frame.TransmittedCells));
    }

    private static double Percentile(IEnumerable<double> source, int percentile)
    {
        double[] values = source.Order().ToArray();
        if (values.Length == 0) return 0;
        int index = (int)Math.Ceiling(percentile / 100d * values.Length) - 1;
        return values[Math.Clamp(index, 0, values.Length - 1)];
    }

    private readonly record struct AggregateResult(double P50, double P95, double P99, double Full, double Partial, double Render, double Viewport, double SyntaxTime, double Draw, double Lines, double Input, double Classification, double Present, double Alloc, double Dirty, double OutputCalls, double Transmitted);
}
