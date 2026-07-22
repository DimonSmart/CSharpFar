using System.Text;
using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

internal sealed class ApplicationTestRunBuilder
{
    private static readonly object ActiveRunsLock = new();
    private static readonly HashSet<Application> ActiveRuns = [];

    private readonly Application _application;
    private readonly FakeConsoleDriver _driver;
    private readonly List<RunStep> _steps = [];
    private int _nextStep;
    private bool _started;
    private bool _waitingForApplicationReady;

    private ApplicationTestRunBuilder(Application application, FakeConsoleDriver driver) =>
        (_application, _driver) = (application, driver);

    public static ApplicationTestRunBuilder For(Application application, FakeConsoleDriver driver) =>
        new(application, driver);

    public ApplicationTestRunBuilder Input(ConsoleInputEvent input)
    {
        _steps.Add(new InputStep(input));
        return this;
    }

    public ApplicationTestRunBuilder Key(ConsoleKeyInfo key) =>
        Input(new KeyConsoleInputEvent(key));

    public ApplicationTestRunBuilder Press(
        ConsoleKey key,
        char keyChar = '\0',
        bool shift = false,
        bool alt = false,
        bool control = false) =>
        Key(new ConsoleKeyInfo(keyChar, key, shift, alt, control));

    public ApplicationTestRunBuilder TypeText(string text)
    {
        foreach (char ch in text)
            Press(ConsoleKey.None, ch);

        return this;
    }

    public ApplicationTestRunBuilder WaitForApplicationReady()
    {
        _steps.Add(ApplicationReadyBarrierStep.Instance);
        return this;
    }

    public ApplicationTestRunBuilder ExitWhenApplicationReady() =>
        WaitForApplicationReady().Press(ConsoleKey.F10);

    public void Run()
    {
        EnsureNotStarted();
        RegisterActiveRun();
        _application.ApplicationInputRequested += OnApplicationInputRequested;

        try
        {
            ActivateUntilBarrier();
            _application.Run();
            EnsureScenarioCompleted();
        }
        finally
        {
            _application.ApplicationInputRequested -= OnApplicationInputRequested;
            UnregisterActiveRun();
        }
    }

    private void EnsureNotStarted()
    {
        if (_started)
            throw new InvalidOperationException("Application test run builder cannot be run more than once.");

        _started = true;
    }

    private void RegisterActiveRun()
    {
        lock (ActiveRunsLock)
        {
            if (!ActiveRuns.Add(_application))
                throw new InvalidOperationException("Another application test run builder is already running for this application.");
        }
    }

    private void UnregisterActiveRun()
    {
        lock (ActiveRunsLock)
        {
            ActiveRuns.Remove(_application);
        }
    }

    private void OnApplicationInputRequested()
    {
        if (!_waitingForApplicationReady || _driver.PendingInputCount != 0)
            return;

        _waitingForApplicationReady = false;
        _nextStep++;
        ActivateUntilBarrier();
    }

    private void ActivateUntilBarrier()
    {
        while (_nextStep < _steps.Count)
        {
            RunStep step = _steps[_nextStep];
            if (step is ApplicationReadyBarrierStep)
            {
                _waitingForApplicationReady = true;
                return;
            }

            var input = ((InputStep)step).Input;
            _driver.EnqueueInput(input);
            _nextStep++;
        }
    }

    private void EnsureScenarioCompleted()
    {
        if (_nextStep >= _steps.Count)
            return;

        throw new InvalidOperationException(BuildIncompleteScenarioMessage());
    }

    private string BuildIncompleteScenarioMessage()
    {
        var message = new StringBuilder();
        message.AppendLine("Application test run completed before all scripted steps were activated.");
        message.AppendLine($"Remaining steps: {_steps.Count - _nextStep}");
        message.AppendLine($"Remaining step: {_steps[_nextStep].GetType().Name}");
        message.AppendLine($"Waiting for application ready: {_waitingForApplicationReady}");
        message.AppendLine($"Pending driver input: {_driver.PendingInputCount}");
        message.Append($"Last dequeued input: {FormatInput(_driver.LastDequeuedInput)}");
        return message.ToString();
    }

    private static string FormatInput(ConsoleInputEvent? input) =>
        input switch
        {
            null => "<none>",
            KeyConsoleInputEvent key => $"Key {key.Key.Key} Char U+{(int)key.Key.KeyChar:X4}",
            _ => input.GetType().Name,
        };

    private abstract record RunStep;

    private sealed record InputStep(ConsoleInputEvent Input) : RunStep;

    private sealed record ApplicationReadyBarrierStep : RunStep
    {
        public static ApplicationReadyBarrierStep Instance { get; } = new();
    }
}
