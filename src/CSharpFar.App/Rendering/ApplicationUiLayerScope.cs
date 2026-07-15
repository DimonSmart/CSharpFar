using CSharpFar.Ui;
using System.Runtime.ExceptionServices;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationUiLayerScope : IDisposable
{
    private readonly IDisposable[] _registrations;
    private bool _disposed;

    public ApplicationUiLayerScope(
        UiCompositionHost composition,
        CommandCompletionLayer commandCompletion,
        PanelQuickSearchLayer panelQuickSearch,
        TopMenuLayer topMenu)
    {
        var registrations = new List<IDisposable>(3);
        try
        {
            registrations.Add(composition.RegisterOverlay(commandCompletion));
            registrations.Add(composition.RegisterOverlay(panelQuickSearch));
            registrations.Add(composition.RegisterOverlay(topMenu));
            _registrations = [.. registrations];
        }
        catch (Exception registrationError)
        {
            RethrowRegistrationErrorAfterRollback(registrationError, registrations);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        for (int i = _registrations.Length - 1; i >= 0; i--)
            _registrations[i].Dispose();

        _disposed = true;
    }

    internal static void RethrowRegistrationErrorAfterRollback(
        Exception registrationError,
        IReadOnlyList<IDisposable> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrationError);
        ArgumentNullException.ThrowIfNull(registrations);

        List<Exception>? errors = null;
        for (int i = registrations.Count - 1; i >= 0; i--)
        {
            try
            {
                registrations[i].Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        if (errors is not null)
            registrationError.Data["ApplicationUiLayerScope.RollbackErrors"] = errors.ToArray();

        ExceptionDispatchInfo.Capture(registrationError).Throw();
    }
}
