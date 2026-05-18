namespace CSharpFar.FarNetHost;

public sealed record FarNetModuleHostOptions
{
    public string ModulesRoot { get; init; } =
        Path.Combine(AppContext.BaseDirectory, "FarNet", "Modules");

    public string? DataRoot { get; init; }

    public bool EnablePanelTools { get; init; }
}
