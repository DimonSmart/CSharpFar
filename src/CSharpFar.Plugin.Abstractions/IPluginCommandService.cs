namespace CSharpFar.Plugin.Abstractions;

public interface IPluginCommandService
{
    void RegisterCommandPrefix(string prefix, ICSharpFarPlugin plugin);
}
