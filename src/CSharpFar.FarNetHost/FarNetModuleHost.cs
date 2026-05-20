using System.Reflection;
using FarNet;

namespace CSharpFar.FarNetHost;

public sealed class FarNetModuleHost : IDisposable
{
    private static readonly ModuleToolOptions SupportedMenuOptions =
        ModuleToolOptions.Panels | ModuleToolOptions.Disk;

    private readonly FarNetModuleHostOptions _options;
    private readonly object _loadGate = new();
    private readonly Dictionary<Guid, IModuleAction> _actions = new();
    private readonly Dictionary<string, FarNetModuleManager> _managersByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FarNetModuleToolAction> _tools = [];
    private readonly List<FarNetModuleCommandAction> _commands = [];
    private readonly List<ModuleHostRegistration> _moduleHosts = [];
    private readonly List<FarNetModuleDiagnostic> _diagnostics = [];
    private readonly List<FarNetModuleLoadContext> _loadContexts = [];
    private bool _loaded;
    private bool _moduleHostsConnected;
    private FarNetModuleHostServices? _services;
    private CSharpFarFarNetApi? _currentApi;

    public FarNetModuleHost()
        : this(new FarNetModuleHostOptions())
    {
    }

    public FarNetModuleHost(string modulesRoot)
        : this(new FarNetModuleHostOptions { ModulesRoot = modulesRoot })
    {
    }

    public FarNetModuleHost(FarNetModuleHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModulesRoot);
        _options = options;
    }

    public IReadOnlyList<FarNetModuleDiagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<FarNetModuleMenuItem> MenuItems
    {
        get
        {
            EnsureLoaded();
            var menuItems = _tools
                .Where(tool => HasOption(tool.Options, ModuleToolOptions.Panels))
                .Select(ToModuleMenuItem)
                .ToList();
            if (_diagnostics.Count > 0)
                menuItems.Add(ToDiagnosticsMenuItem());
            return menuItems;
        }
    }

    public IReadOnlyList<FarNetModuleMenuItem> DiskMenuItems
    {
        get
        {
            EnsureLoaded();
            return _tools
                .Where(tool => HasOption(tool.Options, ModuleToolOptions.Disk))
                .Select(ToModuleMenuItem)
                .ToArray();
        }
    }

    public IReadOnlyList<string> CommandPrefixes
    {
        get
        {
            EnsureLoaded();
            return _commands
                .Select(command => command.Prefix)
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public void Initialize(FarNetModuleHostServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(services.DataRoot);
        _services = services;
        EnsureLoaded();
        InstallFarApi();
        ConnectModuleHosts();
    }

    public FarNetModuleOpenResult OpenFromMenu(Guid actionId)
    {
        EnsureLoaded();
        InstallFarApi();

        if (actionId == FarNetModuleIds.DiagnosticsActionId)
            return ShowDiagnostics();

        return InvokeTool(actionId, new ModuleToolEventArgs { From = ModuleToolOptions.Panels });
    }

    public FarNetModuleOpenResult OpenFromDiskMenu(Guid actionId, bool isLeft)
    {
        EnsureLoaded();
        InstallFarApi();

        return InvokeTool(
            actionId,
            new ModuleToolEventArgs
            {
                From = ModuleToolOptions.Disk,
                IsLeft = isLeft,
            });
    }

    public FarNetModuleOpenResult OpenFromCommandLine(string commandLine)
    {
        EnsureLoaded();
        InstallFarApi();

        return InvokeCommand(commandLine);
    }

    public void Dispose()
    {
        DisconnectModuleHosts();
        Far.ResetApi();
        _currentApi = null;
        _services = null;
        _tools.Clear();
        _commands.Clear();
        _moduleHosts.Clear();
        _actions.Clear();
        _managersByName.Clear();
        foreach (var loadContext in _loadContexts)
            loadContext.Unload();

        _loadContexts.Clear();
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        lock (_loadGate)
        {
            if (_loaded)
                return;

            LoadModules();
            _loaded = true;
        }
    }

    private void LoadModules()
    {
        if (!Directory.Exists(_options.ModulesRoot))
            return;

        foreach (string moduleDirectory in Directory.GetDirectories(_options.ModulesRoot))
        {
            string moduleName = Path.GetFileName(moduleDirectory);
            string assemblyPath = Path.Combine(moduleDirectory, moduleName + ".dll");
            if (!File.Exists(assemblyPath))
            {
                AddDiagnostic(moduleName, $"Expected module assembly '{assemblyPath}' was not found.");
                continue;
            }

            LoadModule(moduleName, assemblyPath);
        }
    }

    private void LoadModule(string moduleName, string assemblyPath)
    {
        try
        {
            var loadContext = new FarNetModuleLoadContext(moduleName, assemblyPath);
            _loadContexts.Add(loadContext);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            ValidateReferencedAssemblies(moduleName, assembly, Path.GetDirectoryName(assemblyPath)!);
            string dataRoot = GetDataRoot();
            var manager = new FarNetModuleManager(moduleName, assembly, dataRoot, _actions, AddDiagnostic);
            _managersByName[moduleName] = manager;

            foreach (Type type in GetExportedTypes(moduleName, assembly))
                TryRegisterModuleItem(moduleName, manager, type);
        }
        catch (Exception ex)
        {
            AddDiagnostic(moduleName, Unwrap(ex).Message);
        }
    }

    private void ValidateReferencedAssemblies(string moduleName, Assembly assembly, string moduleDirectory)
    {
        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            if (string.Equals(reference.Name, "FarNet", StringComparison.OrdinalIgnoreCase))
            {
                if (!FarNetAssemblyCompatibility.IsSupported(reference))
                {
                    AddDiagnostic(
                        moduleName,
                        $"Module references FarNet {reference.Version}, but CSharpFar supports {FarNetAssemblyCompatibility.SupportedVersion}.");
                }
                continue;
            }

            if (FarNetAssemblyCompatibility.IsFrameworkAssembly(reference))
                continue;

            string dependencyPath = Path.Combine(moduleDirectory, reference.Name + ".dll");
            if (!File.Exists(dependencyPath))
                AddDiagnostic(moduleName, $"Module dependency '{reference.Name}' was not found in '{moduleDirectory}'.");
        }
    }

    private IEnumerable<Type> GetExportedTypes(string moduleName, Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            foreach (Exception loaderException in ex.LoaderExceptions.OfType<Exception>())
                AddDiagnostic(moduleName, loaderException.Message);

            return ex.Types.OfType<Type>();
        }
    }

    private void TryRegisterModuleItem(string moduleName, FarNetModuleManager manager, Type type)
    {
        if (type.IsAbstract)
            return;

        if (typeof(ModuleTool).IsAssignableFrom(type))
        {
            TryRegisterTool(moduleName, manager, type);
            return;
        }

        if (typeof(ModuleCommand).IsAssignableFrom(type))
        {
            TryRegisterCommand(moduleName, manager, type);
            return;
        }

        if (typeof(ModuleHost).IsAssignableFrom(type))
        {
            TryCreateModuleHost(moduleName, type);
            return;
        }

        if (typeof(ModuleAction).IsAssignableFrom(type))
            AddDiagnostic(moduleName, $"{type.FullName} is a FarNet {type.BaseType?.Name} item, but v1 supports only ModuleTool and ModuleCommand.");
    }

    private void TryCreateModuleHost(string moduleName, Type type)
    {
        try
        {
            var moduleHost = (ModuleHost?)Activator.CreateInstance(type);
            if (moduleHost is null)
            {
                AddDiagnostic(moduleName, $"Cannot create FarNet module host '{type.FullName}'.");
                return;
            }

            _moduleHosts.Add(new ModuleHostRegistration(moduleName, moduleHost));
        }
        catch (Exception ex)
        {
            AddDiagnostic(moduleName, Unwrap(ex).Message);
        }
    }

    private void TryRegisterTool(string moduleName, FarNetModuleManager manager, Type type)
    {
        var attribute = type.GetCustomAttribute<ModuleToolAttribute>(inherit: false);
        if (attribute is null)
        {
            AddDiagnostic(moduleName, $"{type.FullName} is a ModuleTool but has no ModuleToolAttribute.");
            return;
        }

        if (!Guid.TryParse(attribute.Id, out Guid actionId) || actionId == Guid.Empty)
        {
            AddDiagnostic(moduleName, $"{type.FullName} has invalid ModuleToolAttribute.Id '{attribute.Id}'.");
            return;
        }

        if (string.IsNullOrWhiteSpace(attribute.Name))
        {
            AddDiagnostic(moduleName, $"{type.FullName} has empty ModuleToolAttribute.Name.");
            return;
        }

        ModuleToolOptions supportedOptions = attribute.Options & SupportedMenuOptions;
        if (supportedOptions == ModuleToolOptions.None)
        {
            AddDiagnostic(moduleName, $"{type.FullName} uses unsupported ModuleToolOptions '{attribute.Options}'.");
            return;
        }

        if (_actions.ContainsKey(actionId))
        {
            AddDiagnostic(moduleName, $"{type.FullName} uses duplicate FarNet action id '{actionId}'.");
            return;
        }

        string name = attribute.Resources
            ? manager.GetString(attribute.Name) ?? attribute.Name
            : attribute.Name;
        var tool = new FarNetModuleToolAction(actionId, CleanMenuText(name), type, manager, supportedOptions);
        _actions.Add(actionId, tool);
        _tools.Add(tool);
    }

    private void TryRegisterCommand(string moduleName, FarNetModuleManager manager, Type type)
    {
        var attribute = type.GetCustomAttribute<ModuleCommandAttribute>(inherit: false);
        if (attribute is null)
        {
            AddDiagnostic(moduleName, $"{type.FullName} is a ModuleCommand but has no ModuleCommandAttribute.");
            return;
        }

        if (!Guid.TryParse(attribute.Id, out Guid actionId) || actionId == Guid.Empty)
        {
            AddDiagnostic(moduleName, $"{type.FullName} has invalid ModuleCommandAttribute.Id '{attribute.Id}'.");
            return;
        }

        if (string.IsNullOrWhiteSpace(attribute.Name))
        {
            AddDiagnostic(moduleName, $"{type.FullName} has empty ModuleCommandAttribute.Name.");
            return;
        }

        if (string.IsNullOrWhiteSpace(attribute.Prefix))
        {
            AddDiagnostic(moduleName, $"{type.FullName} has empty ModuleCommandAttribute.Prefix.");
            return;
        }

        if (_actions.ContainsKey(actionId))
        {
            AddDiagnostic(moduleName, $"{type.FullName} uses duplicate FarNet action id '{actionId}'.");
            return;
        }

        string name = attribute.Resources
            ? manager.GetString(attribute.Name) ?? attribute.Name
            : attribute.Name;
        var command = new FarNetModuleCommandAction(
            actionId,
            CleanMenuText(name),
            attribute.Prefix,
            type,
            manager);
        _actions.Add(actionId, command);
        _commands.Add(command);
    }

    private void InstallFarApi()
    {
        if (_services is null)
            return;

        _currentApi = new CSharpFarFarNetApi(_services, _actions, _managersByName, _options);
        Far.Api = _currentApi;
    }

    private void ConnectModuleHosts()
    {
        if (_moduleHostsConnected || _services is null)
            return;

        foreach (var registration in _moduleHosts)
        {
            try
            {
                registration.Host.Connect();
            }
            catch (Exception ex)
            {
                AddDiagnostic(registration.ModuleName, Unwrap(ex).Message);
            }
        }

        _moduleHostsConnected = true;
    }

    private void DisconnectModuleHosts()
    {
        if (!_moduleHostsConnected)
            return;

        foreach (var registration in Enumerable.Reverse(_moduleHosts))
        {
            try
            {
                registration.Host.Disconnect();
            }
            catch (Exception ex)
            {
                AddDiagnostic(registration.ModuleName, Unwrap(ex).Message);
            }
        }

        _moduleHostsConnected = false;
    }

    private string GetDataRoot()
    {
        if (!string.IsNullOrWhiteSpace(_options.DataRoot))
        {
            Directory.CreateDirectory(_options.DataRoot);
            return _options.DataRoot;
        }

        if (_services is not null)
        {
            Directory.CreateDirectory(_services.DataRoot);
            return _services.DataRoot;
        }

        string fallback = Path.Combine(Path.GetTempPath(), "CSharpFar", "FarNet");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private void AddDiagnostic(string moduleName, string message) =>
        _diagnostics.Add(new FarNetModuleDiagnostic(moduleName, message));

    private static FarNetModuleMenuItem ToModuleMenuItem(FarNetModuleToolAction tool) =>
        new(tool.Id, tool.Name, ExtractHotKey(tool.Name));

    private static FarNetModuleMenuItem ToDiagnosticsMenuItem() =>
        new(FarNetModuleIds.DiagnosticsActionId, "FarNet diagnostics...", null);

    private FarNetModuleOpenResult ShowDiagnostics()
    {
        string text = _diagnostics.Count == 0
            ? "No FarNet diagnostics."
            : string.Join(
                Environment.NewLine,
                _diagnostics.Select(diagnostic => diagnostic.ModuleName + ": " + diagnostic.Message));

        if (_services is null)
            return FarNetModuleOpenResult.Failed(text);

        _services.Ui.ShowMessage("FarNet diagnostics", text, ["OK"]);
        return FarNetModuleOpenResult.Completed();
    }

    private FarNetModuleOpenResult InvokeTool(Guid actionId, ModuleToolEventArgs eventArgs)
    {
        if (!_actions.TryGetValue(actionId, out var action) ||
            action is not FarNetModuleToolAction tool)
        {
            return FarNetModuleOpenResult.Failed($"FarNet tool '{actionId}' is not registered.");
        }

        if (!IsSupportedOpen(tool, eventArgs.From))
            return FarNetModuleOpenResult.NoPanel();

        try
        {
            tool.Invoke(this, eventArgs);
            return ToOpenResultAfterTool();
        }
        catch (Exception ex)
        {
            var error = Unwrap(ex);
            AddDiagnostic(Path.GetFileNameWithoutExtension(tool.ToolType.Assembly.Location), error.Message);
            return FarNetModuleOpenResult.Failed(error.Message);
        }
    }

    private FarNetModuleOpenResult InvokeCommand(string commandLine)
    {
        string prefix = GetCommandPrefix(commandLine);
        var command = _commands.FirstOrDefault(
            candidate => string.Equals(candidate.Prefix, prefix, StringComparison.OrdinalIgnoreCase));
        if (command is null)
            return FarNetModuleOpenResult.NoPanel();

        try
        {
            command.Invoke(
                this,
                new ModuleCommandEventArgs(commandLine)
                {
                    Prefix = command.Prefix,
                });
            return ToOpenResultAfterModuleAction();
        }
        catch (Exception ex)
        {
            var error = Unwrap(ex);
            AddDiagnostic(Path.GetFileNameWithoutExtension(command.CommandType.Assembly.Location), error.Message);
            return FarNetModuleOpenResult.Failed(error.Message);
        }
    }

    private FarNetModuleOpenResult ToOpenResultAfterTool() =>
        ToOpenResultAfterModuleAction();

    private FarNetModuleOpenResult ToOpenResultAfterModuleAction()
    {
        var panel = _currentApi?.ConsumePendingPanel();
        return panel is null
            ? FarNetModuleOpenResult.Completed()
            : FarNetModuleOpenResult.OpenedPanel(new FarNetPanelAdapter(panel));
    }

    private static bool IsSupportedOpen(FarNetModuleToolAction tool, ModuleToolOptions from) =>
        HasOption(tool.Options, from);

    private static bool HasOption(ModuleToolOptions options, ModuleToolOptions option) =>
        (options & option) != 0;

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: not null } ||
               exception is TypeInitializationException { InnerException: not null })
        {
            exception = exception.InnerException!;
        }

        return exception;
    }

    private static string CleanMenuText(string text) =>
        text.Replace("&", string.Empty, StringComparison.Ordinal);

    private static char? ExtractHotKey(string text)
    {
        int marker = text.IndexOf('&', StringComparison.Ordinal);
        if (marker >= 0 && marker + 1 < text.Length)
            return text[marker + 1];

        return null;
    }

    private static string GetCommandPrefix(string commandLine)
    {
        int index = 0;
        while (index < commandLine.Length &&
               commandLine[index] != ':' &&
               !char.IsWhiteSpace(commandLine[index]))
        {
            index++;
        }

        return commandLine[..index];
    }

    private readonly record struct ModuleHostRegistration(string ModuleName, ModuleHost Host);
}
