using FarNet;

namespace CSharpFar.FarNetHost;

internal sealed class FarNetModuleToolAction : IModuleTool
{
    public FarNetModuleToolAction(
        Guid id,
        string name,
        Type toolType,
        FarNetModuleManager manager,
        ModuleToolOptions options)
    {
        Id = id;
        Name = name;
        ToolType = toolType;
        Manager = manager;
        Options = options;
        DefaultOptions = options;
    }

    public Guid Id { get; }
    public string Name { get; }
    public Type ToolType { get; }
    public IModuleManager Manager { get; }
    public ModuleItemKind Kind => ModuleItemKind.Tool;
    public ModuleToolOptions Options { get; set; }
    public ModuleToolOptions DefaultOptions { get; }

    public void Invoke(object sender, ModuleToolEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var instance = (ModuleTool?)Activator.CreateInstance(ToolType);
        if (instance is null)
            throw new ModuleException($"Cannot create FarNet tool '{ToolType.FullName}'.");

        instance.Invoke(sender, e);
    }
}
