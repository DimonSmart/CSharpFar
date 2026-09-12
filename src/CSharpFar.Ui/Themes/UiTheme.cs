namespace CSharpFar.Ui;

public static class UiTheme
{
    private static readonly AsyncLocal<ConsolePalette?> s_temporary = new();
    private static ConsolePalette? s_current;

    public static ConsolePalette Current => s_temporary.Value ?? s_current ?? PaletteRegistry.Default;

    public static void Initialize(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (s_current is not null && !ReferenceEquals(s_current, palette))
            throw new InvalidOperationException("UI theme is already initialized.");

        s_current = palette;
    }

    public static IDisposable UseTemporary(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var previous = s_temporary.Value;
        s_temporary.Value = palette;
        return new TemporaryThemeScope(previous);
    }

    internal static void ResetForTests()
    {
        s_current = null;
        s_temporary.Value = null;
    }

    private sealed class TemporaryThemeScope(ConsolePalette? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            s_temporary.Value = previous;
            _disposed = true;
        }
    }
}
