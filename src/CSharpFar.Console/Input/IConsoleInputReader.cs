using System.Diagnostics.CodeAnalysis;

namespace CSharpFar.Console.Input;

public interface IConsoleInputReader : IConsoleInputDiagnostics, IDisposable
{
    ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default);

    bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent);

    ConsoleKeyInfo ReadKey(bool intercept);

    void SuspendInputMode();

    void RestoreInputMode();
}
