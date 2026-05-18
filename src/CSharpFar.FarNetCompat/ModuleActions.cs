using System.Globalization;
using System.Reflection;

namespace FarNet;

public abstract class BaseModuleItem
{
    private IModuleManager? _manager;

    public IModuleManager Manager => _manager ??= Far.Api.GetModuleManager(GetType());

    public string? GetString(string name) => Manager.GetString(name);

    public string GetHelpTopic(string topic)
    {
        string path = Path.GetDirectoryName(GetType().Assembly.Location) ?? string.Empty;
        return "<" + path + "\\>" + topic;
    }

    public void ShowHelpTopic(string topic)
    {
        string path = Path.GetDirectoryName(GetType().Assembly.Location) ?? string.Empty;
        Far.Api.ShowHelp(path, topic, HelpOptions.Path);
    }
}

public abstract class ModuleAction : BaseModuleItem
{
}

public abstract class ModuleTool : ModuleAction
{
    public abstract void Invoke(object sender, ModuleToolEventArgs e);
}

public abstract class ModuleCommand : ModuleAction
{
    public abstract void Invoke(object sender, ModuleCommandEventArgs e);

    protected void InvokeSubcommand(
        string command,
        Func<ReadOnlySpan<char>, CommandParameters, Subcommand?> factory)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(factory);

        ReadOnlySpan<char> text = command.AsSpan().Trim();
        int colon = text.IndexOf(':');
        if (colon >= 0)
            text = text[(colon + 1)..].TrimStart();

        var parameters = CommandParameters.Parse(text);
        var subcommand = factory(parameters.Command, parameters);
        if (subcommand is null)
            throw new ModuleException($"Unknown command '{parameters.Command.ToString()}'.");

        parameters.ThrowUnknownParameters();
        subcommand.Invoke();
    }
}

public abstract class Subcommand
{
    public abstract void Invoke();
}

public abstract class ModuleEditor : ModuleAction
{
}

public abstract class ModuleDrawer : ModuleAction
{
}

public abstract class ModuleHost : BaseModuleItem
{
    public virtual void Connect()
    {
    }

    public virtual void Disconnect()
    {
    }

    public virtual object? Interop(string command, object? args) => null;
}

public abstract class ModuleActionAttribute : Attribute, ICloneable
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool Resources { get; set; }

    public object Clone() => MemberwiseClone();
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleToolAttribute : ModuleActionAttribute
{
    public ModuleToolOptions Options { get; set; }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleCommandAttribute : ModuleActionAttribute
{
    public string Prefix { get; set; } = null!;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleEditorAttribute : ModuleActionAttribute
{
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleDrawerAttribute : ModuleActionAttribute
{
}

[Flags]
public enum ModuleToolOptions
{
    None = 0,
    Config = 1 << 0,
    Disk = 1 << 1,
    Editor = 1 << 2,
    Panels = 1 << 3,
    Viewer = 1 << 4,
    Dialog = 1 << 5,
    F11Menus = Panels | Editor | Viewer | Dialog,
    AllMenus = F11Menus | Disk,
    AllAreas = AllMenus | Config,
}

public enum ModuleItemKind
{
    Command,
    Drawer,
    Editor,
    Tool,
}

public sealed class ModuleToolEventArgs : EventArgs
{
    public ModuleToolOptions From { get; set; }
    public bool Ignore { get; set; }
    public bool IsLeft { get; set; }
}

public class ModuleCommandEventArgs : EventArgs
{
    public ModuleCommandEventArgs()
        : this(string.Empty)
    {
    }

    public ModuleCommandEventArgs(string command)
    {
        Command = command;
    }

    public string Command { get; set; }
    public string? Prefix { get; set; }
    public bool IsMacro { get; set; }
    public bool Ignore { get; set; }
}

public interface IModuleAction
{
    Guid Id { get; }
    string Name { get; }
    IModuleManager Manager { get; }
    ModuleItemKind Kind { get; }
}

public interface IModuleTool : IModuleAction
{
    void Invoke(object sender, ModuleToolEventArgs e);
    ModuleToolOptions Options { get; set; }
    ModuleToolOptions DefaultOptions { get; }
}

public interface IModuleCommand : IModuleAction
{
}

public interface IModuleDrawer : IModuleAction
{
}

public interface IModuleEditor : IModuleAction
{
}

public abstract class IModuleManager
{
    public abstract CultureInfo CurrentUICulture { get; set; }
    public abstract string? GetString(string name);
    public abstract string GetFolderPath(SpecialFolder folder, bool create);
    public abstract void Unregister();
    public abstract IModuleCommand RegisterCommand(ModuleCommandAttribute attribute, EventHandler<ModuleCommandEventArgs> handler);
    public abstract IModuleDrawer RegisterDrawer(ModuleDrawerAttribute attribute, Action<IEditor, ModuleDrawerEventArgs> handler);
    public abstract IModuleTool RegisterTool(ModuleToolAttribute attribute, EventHandler<ModuleToolEventArgs> handler);
    public abstract string ModuleName { get; }
    public abstract string StoredUICulture { get; set; }
    public abstract Assembly LoadAssembly(bool connect);
    public abstract void SaveConfig();
    public abstract object? Interop(string command, object? args);
}

public sealed class ModuleDrawerEventArgs : EventArgs
{
}
