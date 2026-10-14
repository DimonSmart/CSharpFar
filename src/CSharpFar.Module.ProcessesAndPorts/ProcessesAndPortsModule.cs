using System.Net.NetworkInformation;
using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;
using CSharpFar.Plugin.Abstractions;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.Module.ProcessesAndPorts;

public sealed class ProcessesAndPortsModule(IProcessesAndPortsPlatformService platform) : ICSharpFarPlugin
{
    private readonly IProcessesAndPortsPlatformService _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    public ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(host);
        host.RegisterCommand(new PluginCommandDescriptor
        {
            Id = "open",
            Title = "Processes and Ports",
            ExecuteAsync = (context, _) => ValueTask.FromResult(
                ModulePluginBridge.ToCommandResult(
                    context.Origin == PluginCommandOrigin.CommandLine
                        ? OpenFromCommandLine(context.TargetPanelSide, context.RawCommandLine ?? "ports")
                        : OpenFromMenu(context.TargetPanelSide))),
        });
        host.RegisterMenuItem(new PluginMenuItemDescriptor
        {
            Id = ProcessesAndPortsModuleIds.MenuActionId.ToString("D"),
            ActionId = ProcessesAndPortsModuleIds.MenuActionId,
            Title = "Processes and Ports...",
            CommandId = "open",
            HotKey = 'P',
        });
        host.RegisterCommandPrefix("ports", "open");
        return ValueTask.CompletedTask;
    }

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
