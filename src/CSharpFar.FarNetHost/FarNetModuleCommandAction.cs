using FarNet;

namespace CSharpFar.FarNetHost;

internal sealed class FarNetModuleCommandAction : IModuleCommand
{
    public FarNetModuleCommandAction(
        Guid id,
        string name,
        string prefix,
        Type commandType,
        FarNetModuleManager manager)
    {
        Id = id;
        Name = name;
        Prefix = prefix;
        CommandType = commandType;
        Manager = manager;
    }

    public Guid Id { get; }
    public string Name { get; }
    public string Prefix { get; }
    public Type CommandType { get; }
    public IModuleManager Manager { get; }
    public ModuleItemKind Kind => ModuleItemKind.Command;

    public void Invoke(object sender, ModuleCommandEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var instance = (ModuleCommand?)Activator.CreateInstance(CommandType);
        if (instance is null)
            throw new ModuleException($"Cannot create FarNet command '{CommandType.FullName}'.");

        instance.Invoke(sender, e);
    }
}
