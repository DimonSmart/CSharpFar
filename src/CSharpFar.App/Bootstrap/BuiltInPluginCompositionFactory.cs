using CSharpFar.Module.Abstractions;
using CSharpFar.Module.Archives;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.NetworkTraffic;
using CSharpFar.Module.ProcessesAndPorts;
using CSharpFar.Module.Sftp;
using CSharpFar.NetworkTraffic;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Bootstrap;

// Built-ins are registered in a deterministic order before external plugins.
internal static class BuiltInPluginCompositionFactory
{
    public static List<(PluginDescriptor Descriptor, ICSharpFarPlugin Plugin)> Create(
        bool enableBuiltInNetworkModules,
        ModuleStartupInfo moduleStartup,
        ModuleUiServices moduleUiServices,
        SftpModule? sftpModule,
        FtpModule? ftpModule,
        IProcessesAndPortsPlatformService? processesAndPorts,
        Func<INetworkTrafficMonitor>? networkTrafficMonitorFactory)
    {
        var pluginComposition = new List<(PluginDescriptor Descriptor, ICSharpFarPlugin Plugin)>();
        if (enableBuiltInNetworkModules)
        {
            var sftp = sftpModule ?? new SftpModule();
            sftp.Initialize(moduleStartup);
            pluginComposition.Add((
                new PluginDescriptor { Id = SftpModuleIds.ModuleId.ToString("D"), Title = "SFTP" }, sftp));

            var ftp = ftpModule ?? new FtpModule();
            ftp.Initialize(moduleStartup);
            pluginComposition.Add((
                new PluginDescriptor { Id = FtpModuleIds.ModuleId.ToString("D"), Title = "FTP/FTPS" }, ftp));
        }

        var ports = new ProcessesAndPortsModule(
            processesAndPorts ?? new UnsupportedProcessesAndPortsPlatformService());
        ports.Initialize(moduleStartup);
        pluginComposition.Add((
            new PluginDescriptor
            {
                Id = ProcessesAndPortsModuleIds.MenuActionId.ToString("D"),
                Title = "Processes and Ports",
            }, ports));

        pluginComposition.Add((
            new PluginDescriptor { Id = NetworkTrafficPlugin.Id, Title = "Network Traffic" },
            new NetworkTrafficPlugin(moduleUiServices, networkTrafficMonitorFactory)));

        pluginComposition.Add((
            new PluginDescriptor { Id = ArchivePlugin.Id, Title = "Archives" },
            new ArchivePlugin()));
        return pluginComposition;
    }
}
