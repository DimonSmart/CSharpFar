using CSharpFar.Core.Abstractions;

namespace CSharpFar.Plugin.Abstractions;

public interface IPluginPanel : IFilePanelSource, IDisposable
{
    PluginPanelInfo GetOpenPanelInfo();
}
