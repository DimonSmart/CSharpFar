using System.Net.NetworkInformation;
using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.Module.ProcessesAndPorts;

public sealed class ProcessesAndPortsModule(IProcessesAndPortsPlatformService platform)
{
    private readonly IProcessesAndPortsPlatformService _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    private ModuleStartupInfo? _startupInfo;

    public void Initialize(ModuleStartupInfo startupInfo) => _startupInfo = startupInfo ?? throw new ArgumentNullException(nameof(startupInfo));
    public ModuleActionResult OpenFromMenu(PanelSide side) => Open(null);
    public ModuleActionResult OpenFromCommandLine(PanelSide side, string commandLine)
    {
        int separator = commandLine.IndexOfAny([' ', ':', '\t']);
        return Open(separator < 0 ? null : commandLine[(separator + 1)..].Trim());
    }

    private ModuleActionResult Open(string? filter)
    {
        if (_startupInfo is null) throw new InvalidOperationException("Processes and Ports module startup info was not set.");
        if (!_platform.Support.IsSupported)
        {
            _startupInfo.Ui.ShowMessage("Processes and Ports", _platform.Support.Reason ?? "Not supported on this platform.");
            return ModuleActionResult.Completed();
        }
        new ProcessesAndPortsDialog(_startupInfo.Ui, _platform).Show(filter);
        return ModuleActionResult.Completed();
    }
}
