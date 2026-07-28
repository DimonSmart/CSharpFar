using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFar.Console.Input;

namespace CSharpFar.DemoRecorder;

public sealed record DemoScenario(
    string Name,
    int ViewportWidth,
    int ViewportHeight,
    int FramesPerSecond,
    int DefaultHoldMs,
    string ScreenshotFileName,
    string GifFileName,
    string Mp4FileName,
    DemoRenderOptions Render,
    IReadOnlyList<DemoScenarioStep> Steps)
{
    public static DemoScenario Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Demo scenario file does not exist.", path);

        DemoScenarioFile file = JsonSerializer.Deserialize<DemoScenarioFile>(
            File.ReadAllText(path),
            SerializerOptions())
            ?? throw new InvalidOperationException($"Failed to parse scenario file: {path}");

        return new DemoScenario(
            file.Name,
            file.ViewportWidth,
            file.ViewportHeight,
            file.FramesPerSecond,
            file.DefaultHoldMs,
            file.ScreenshotFileName,
            file.GifFileName,
            file.Mp4FileName,
            new DemoRenderOptions(
                file.Render.CellWidth,
                file.Render.CellHeight,
                file.Render.FontFamily,
                file.Render.FontSize,
                file.Render.Padding),
            file.Steps.Select(DemoScenarioStepFactory.Create).ToArray());
    }

    private static JsonSerializerOptions SerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed record DemoRenderOptions(
    int CellWidth,
    int CellHeight,
    string FontFamily,
    float FontSize,
    int Padding);

public abstract record DemoScenarioStep;

public sealed record DemoKeyStep(ConsoleInputEvent Input, int HoldMs) : DemoScenarioStep;
public sealed record DemoTextStep(string Text, int HoldMs) : DemoScenarioStep;
public sealed record DemoWaitStep(int DurationMs) : DemoScenarioStep;
public sealed record DemoExpectTextStep(string Text) : DemoScenarioStep;
public sealed record DemoScreenshotStep(string FileName) : DemoScenarioStep;

internal static class DemoScenarioStepFactory
{
    public static DemoScenarioStep Create(DemoScenarioFileStep step)
    {
        return step.Type switch
        {
            "key" => new DemoKeyStep(ParseKey(step.Key ?? throw Missing("key")), step.HoldMs ?? 600),
            "text" => new DemoTextStep(step.Text ?? string.Empty, step.HoldMs ?? 600),
            "wait" => new DemoWaitStep(step.DurationMs ?? throw Missing("durationMs")),
            "expectText" => new DemoExpectTextStep(step.Text ?? throw Missing("text")),
            "screenshot" => new DemoScreenshotStep(step.FileName ?? throw Missing("fileName")),
            _ => throw new InvalidOperationException($"Unsupported scenario step type '{step.Type}'."),
        };
    }

    private static Exception Missing(string field) =>
        new InvalidOperationException($"Scenario step is missing required field '{field}'.");

    private static ConsoleInputEvent ParseKey(string keyText)
    {
        string[] parts = keyText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool shift = parts.Any(static p => p.Equals("Shift", StringComparison.OrdinalIgnoreCase));
        bool alt = parts.Any(static p => p.Equals("Alt", StringComparison.OrdinalIgnoreCase));
        bool control = parts.Any(static p => p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase));

        string keyName = parts.Last();
        ConsoleKey key = keyName.ToUpperInvariant() switch
        {
            "TAB" => ConsoleKey.Tab,
            "ENTER" => ConsoleKey.Enter,
            "ESC" or "ESCAPE" => ConsoleKey.Escape,
            "UP" => ConsoleKey.UpArrow,
            "DOWN" => ConsoleKey.DownArrow,
            "LEFT" => ConsoleKey.LeftArrow,
            "RIGHT" => ConsoleKey.RightArrow,
            "PGDN" or "PAGEDOWN" => ConsoleKey.PageDown,
            "PGUP" or "PAGEUP" => ConsoleKey.PageUp,
            "HOME" => ConsoleKey.Home,
            "END" => ConsoleKey.End,
            "SPACE" => ConsoleKey.Spacebar,
            _ when keyName.Length >= 2 && keyName[0] is 'F' or 'f' && int.TryParse(keyName[1..], out int fn) && fn is >= 1 and <= 12
                => ConsoleKey.F1 + (fn - 1),
            _ when keyName.Length == 1 && char.IsLetter(keyName[0])
                => Enum.Parse<ConsoleKey>(keyName.ToUpperInvariant()),
            _ => throw new InvalidOperationException($"Unsupported key notation '{keyText}'."),
        };

        char keyChar = ResolveKeyChar(keyName, key, shift, control);
        return new KeyConsoleInputEvent(new ConsoleKeyInfo(keyChar, key, shift, alt, control));
    }

    private static char ResolveKeyChar(string keyName, ConsoleKey key, bool shift, bool control)
    {
        if (key == ConsoleKey.Spacebar)
            return ' ';

        if (keyName.Length == 1 && char.IsLetter(keyName[0]))
        {
            char letter = shift ? char.ToUpperInvariant(keyName[0]) : char.ToLowerInvariant(keyName[0]);
            if (control)
                return (char)(char.ToUpperInvariant(letter) - 'A' + 1);

            return letter;
        }

        return '\0';
    }
}

internal sealed record DemoScenarioFile(
    string Name,
    int ViewportWidth,
    int ViewportHeight,
    int FramesPerSecond,
    int DefaultHoldMs,
    string ScreenshotFileName,
    string GifFileName,
    string Mp4FileName,
    DemoRenderFile Render,
    DemoScenarioFileStep[] Steps);

internal sealed record DemoRenderFile(
    int CellWidth,
    int CellHeight,
    string FontFamily,
    float FontSize,
    int Padding);

internal sealed record DemoScenarioFileStep(
    string Type,
    string? Key,
    string? Text,
    int? HoldMs,
    int? DurationMs,
    string? FileName);
