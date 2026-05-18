using CSharpFar.Core.Abstractions;

namespace CSharpFar.Module.Abstractions;

public interface IModulePanel : IFilePanelSource, IDisposable
{
    ModulePanelInfo GetOpenPanelInfo();
}
