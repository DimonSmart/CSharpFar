using CSharpFar.Console.Input;
using CSharpFar.DemoRecorder;

namespace CSharpFar.Tests;

public sealed class DemoScenarioTests
{
    [Fact]
    public void Load_ReadmeScenario_ParsesViewportAndSteps()
    {
        string path = FindScenarioPath();

        DemoScenario scenario = DemoScenario.Load(path);

        Assert.Equal("README demo", scenario.Name);
        Assert.Equal(120, scenario.ViewportWidth);
        Assert.Equal(20, scenario.ViewportHeight);
        Assert.Contains(scenario.Steps, step => step is DemoScreenshotStep screenshot && screenshot.FileName == "csharpfar-demo.png");
    }

    [Fact]
    public void Load_ReadmeScenario_ParsesF10KeySteps()
    {
        string path = FindScenarioPath();

        DemoScenario scenario = DemoScenario.Load(path);

        Assert.Contains(
            scenario.Steps,
            step => step is DemoKeyStep
            {
                Input: KeyConsoleInputEvent
                {
                    Key.Key: ConsoleKey.F10,
                },
            });
    }

    [Theory]
    [InlineData(ConsoleKey.F3, false)]
    [InlineData(ConsoleKey.F4, false)]
    [InlineData(ConsoleKey.F5, false)]
    [InlineData(ConsoleKey.F6, false)]
    [InlineData(ConsoleKey.F6, true)]
    [InlineData(ConsoleKey.F7, false)]
    [InlineData(ConsoleKey.F4, true)]
    [InlineData(ConsoleKey.F8, false)]
    public void Load_ReadmeScenario_ContainsVisibleWorkflowSurfaceKeys(ConsoleKey key, bool shift)
    {
        string path = FindScenarioPath();

        DemoScenario scenario = DemoScenario.Load(path);

        Assert.Contains(
            scenario.Steps,
            step => step is DemoKeyStep
            {
                Input: KeyConsoleInputEvent
                {
                    Key.Key: var actualKey,
                    Key.Modifiers: var modifiers,
                },
            } &&
            actualKey == key &&
            (((modifiers & ConsoleModifiers.Shift) != 0) == shift));
    }

    private static string FindScenarioPath()
    {
        string? current = AppContext.BaseDirectory;
        while (current is not null)
        {
            string candidate = Path.Combine(current, "scripts", "demo", "readme-demo.json");
            if (File.Exists(candidate))
                return candidate;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException("Could not locate scripts/demo/readme-demo.json from test base directory.");
    }
}
