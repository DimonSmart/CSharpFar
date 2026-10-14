using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Plugins;

internal readonly record struct PluginCommandAddress(string PluginId, string CommandId);
internal sealed record PluginMenuRegistration(string PluginId, PluginMenuItemDescriptor Item);
internal sealed record PluginInitializationResult(bool Success, string? Error = null);

/// <summary>Publishes complete registration batches, never an initializing plugin's partial state.</summary>
internal sealed class NativePluginRuntime
{
    private readonly IPluginApplicationContext _application;
    private readonly Func<PluginDescriptor, IPluginSettingsService> _settingsFactory;
    private readonly Func<PluginDescriptor, IPluginUiService> _uiFactory;
    private readonly Func<PluginDescriptor, IPluginCredentialService> _credentialFactory;
    private readonly string _demoRoot = Path.Combine(Path.GetTempPath(), "CSharpFar-Demo-Plugins", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, Registration> _plugins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginCommandAddress> _prefixes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PluginMenuRegistration> _menus = [];
    private bool _sealed;
    private readonly SemaphoreSlim _registrationGate = new(1, 1);

    public NativePluginRuntime(
        IPluginApplicationContext application,
        Func<PluginDescriptor, IPluginSettingsService> settingsFactory,
        Func<PluginDescriptor, IPluginUiService> uiFactory,
        Func<PluginDescriptor, IPluginCredentialService> credentialFactory)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _settingsFactory = settingsFactory ?? throw new ArgumentNullException(nameof(settingsFactory));
        _uiFactory = uiFactory ?? throw new ArgumentNullException(nameof(uiFactory));
        _credentialFactory = credentialFactory ?? throw new ArgumentNullException(nameof(credentialFactory));
    }

    public IReadOnlyList<PluginMenuRegistration> MenuItems => _menus.ToArray();
    public IReadOnlyCollection<string> CommandPrefixes => _prefixes.Keys.ToArray();

    public async ValueTask<PluginInitializationResult> RegisterAsync(
        PluginDescriptor descriptor, ICSharpFarPlugin plugin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(plugin);
        await _registrationGate.WaitAsync(cancellationToken);
        try
        {
            EnsureMutable();
            if (string.IsNullOrWhiteSpace(descriptor.Id) ||
                !descriptor.Id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.') ||
                descriptor.Id is "." or ".." || string.IsNullOrWhiteSpace(descriptor.Title))
                return new(false, "Plugin ID must be a safe single path component and title must not be empty.");
            if (_plugins.ContainsKey(descriptor.Id))
                return new(false, $"Plugin '{descriptor.Id}' is already registered.");

            return await RegisterCoreAsync(descriptor, plugin, cancellationToken);
        }
        finally
        {
            _registrationGate.Release();
        }
    }

    private async ValueTask<PluginInitializationResult> RegisterCoreAsync(
        PluginDescriptor descriptor, ICSharpFarPlugin plugin, CancellationToken cancellationToken)
    {
        var stage = new Registration(descriptor);
        try
        {
            var settings = new ScopedSettings(
                descriptor.Id,
                _application.RunMode == PluginRunMode.Demo
                    ? Path.Combine(_demoRoot, descriptor.Id)
                    : null,
                _application.RunMode == PluginRunMode.Demo ? null : _settingsFactory(descriptor));
            var credentials = _application.RunMode == PluginRunMode.Demo
                ? new DeniedCredentials() : _credentialFactory(descriptor);
            var host = new RegistrationHost(stage, _application, settings, _uiFactory(descriptor), credentials);
            await plugin.InitializeAsync(host, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            stage.Validate(_prefixes);
            var existingActionIds = _menus
                .Where(menu => menu.Item.ActionId.HasValue)
                .Select(menu => menu.Item.ActionId!.Value)
                .ToHashSet();
            foreach (var item in stage.Menus)
            {
                if (item.ActionId == Guid.Empty || (item.ActionId is { } actionId && !existingActionIds.Add(actionId)))
                    throw new InvalidOperationException($"Duplicate or empty plugin action GUID for menu item '{item.Id}'.");
            }

            // A registration becomes visible only after initialization and validation succeed.
            _plugins.Add(descriptor.Id, stage);
            foreach (var prefix in stage.Prefixes)
                _prefixes.Add(prefix.Key, new PluginCommandAddress(descriptor.Id, prefix.Value));
            foreach (var item in stage.Menus)
                _menus.Add(new PluginMenuRegistration(descriptor.Id, item));
            stage.Freeze();
            return new(true);
        }
        catch (Exception e)
        {
            try
            {
                await stage.DisposeProvidersAsync();
            }
            catch (Exception cleanupError)
            {
                return new(false, $"Plugin '{descriptor.Title}' ({descriptor.Id}): {e.Message}. Cleanup: {cleanupError.Message}");
            }
            return new(false, $"Plugin '{descriptor.Title}' ({descriptor.Id}): {e.Message}");
        }
    }

    public void Seal() => _sealed = true;

    public bool TryResolvePrefix(string commandLine, out PluginCommandAddress address, out string arguments)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        int i = 0;
        while (i < commandLine.Length && commandLine[i] != ':' && !char.IsWhiteSpace(commandLine[i]))
            i++;
        bool found = _prefixes.TryGetValue(commandLine[..i], out address);
        arguments = i < commandLine.Length ? commandLine[(i + 1)..] : string.Empty;
        return found;
    }

    public async ValueTask<PluginCommandResult> ExecuteAsync(
        PluginCommandAddress address,
        PluginCommandOrigin origin,
        PanelSide targetPanelSide,
        string? rawCommandLine = null,
        string? arguments = null,
        CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(address.PluginId, out var registration) ||
            !registration.Commands.TryGetValue(address.CommandId, out var command))
            return PluginCommandResult.Failed($"Plugin command '{address.PluginId}/{address.CommandId}' is not registered.");

        var host = registration.Host ?? throw new InvalidOperationException("Plugin host was not initialized.");
        var context = new ExecutionContext(
            host.Application, host.Settings, host.Ui, host.Credentials,
            targetPanelSide, origin, rawCommandLine, arguments);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await command.ExecuteAsync(context, cancellationToken)
                ?? PluginCommandResult.Failed("Plugin command returned no result.");
        }
        catch (OperationCanceledException)
        {
            return PluginCommandResult.Cancelled();
        }
        catch (Exception e)
        {
            return PluginCommandResult.Failed($"Plugin '{registration.Descriptor.Title}': {e.Message}");
        }
    }

    public async ValueTask<PluginCommandResult> OpenProviderAsync(
        string pluginId, string providerId, PanelSide targetPanelSide,
        CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var registration) ||
            !registration.Providers.TryGetValue(providerId, out var provider))
            return PluginCommandResult.Failed($"Panel provider '{pluginId}/{providerId}' is not registered.");
        var host = registration.Host ?? throw new InvalidOperationException("Plugin host was not initialized.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new ProviderContext(host.Application, host.Settings, host.Ui, host.Credentials, targetPanelSide);
            return PluginCommandResult.OpenPanel(await provider.OpenAsync(context, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            return PluginCommandResult.Cancelled();
        }
        catch (Exception e)
        {
            return PluginCommandResult.Failed($"Panel provider '{pluginId}/{providerId}': {e.Message}");
        }
    }

    private void EnsureMutable()
    {
        if (_sealed) throw new InvalidOperationException("Plugin initialization is closed.");
    }

    private sealed class Registration(PluginDescriptor descriptor)
    {
        public PluginDescriptor Descriptor { get; } = descriptor;
        public Dictionary<string, PluginCommandDescriptor> Commands { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IPanelProvider> Providers { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Prefixes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<PluginMenuItemDescriptor> Menus { get; } = [];
        public RegistrationHost? Host { get; set; }
        public bool Frozen { get; private set; }

        public void CheckWritable()
        {
            if (Frozen) throw new InvalidOperationException("Registration is closed.");
        }

        public void Validate(Dictionary<string, PluginCommandAddress> globalPrefixes)
        {
            foreach (var item in Menus)
                if (!Commands.ContainsKey(item.CommandId))
                    throw new InvalidOperationException($"Menu '{item.Id}' references unknown command '{item.CommandId}'.");
            foreach (var entry in Prefixes)
            {
                if (!Commands.ContainsKey(entry.Value))
                    throw new InvalidOperationException($"Prefix '{entry.Key}' references unknown command '{entry.Value}'.");
                if (globalPrefixes.ContainsKey(entry.Key))
                    throw new InvalidOperationException($"Duplicate global command prefix '{entry.Key}'.");
            }
        }

        public void Freeze() => Frozen = true;
        public async ValueTask DisposeProvidersAsync()
        {
            foreach (var provider in Providers.Values)
            {
                if (provider is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (provider is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    private sealed class RegistrationHost : IPluginHost
    {
        private readonly Registration _owner;
        private readonly HashSet<string> _menuIds = new(StringComparer.Ordinal);

        public RegistrationHost(Registration owner, IPluginApplicationContext application,
            IPluginSettingsService settings, IPluginUiService ui, IPluginCredentialService credentials)
        {
            _owner = owner;
            Application = application;
            Settings = settings;
            Ui = ui;
            Credentials = credentials;
            owner.Host = this;
        }

        public IPluginApplicationContext Application { get; }
        public IPluginSettingsService Settings { get; }
        public IPluginUiService Ui { get; }
        public IPluginCredentialService Credentials { get; }

        public void RegisterCommand(PluginCommandDescriptor command)
        {
            _owner.CheckWritable();
            ArgumentNullException.ThrowIfNull(command);
            CheckId(command.Id);
            ArgumentNullException.ThrowIfNull(command.ExecuteAsync);
            if (!_owner.Commands.TryAdd(command.Id, command))
                throw new InvalidOperationException($"Duplicate command '{command.Id}'.");
        }

        public void RegisterMenuItem(PluginMenuItemDescriptor item)
        {
            _owner.CheckWritable();
            ArgumentNullException.ThrowIfNull(item);
            CheckId(item.Id);
            CheckId(item.CommandId);
            if (!_menuIds.Add(item.Id))
                throw new InvalidOperationException($"Duplicate menu item '{item.Id}'.");
            _owner.Menus.Add(item);
        }

        public void RegisterCommandPrefix(string prefix, string commandId)
        {
            _owner.CheckWritable();
            CheckId(prefix);
            CheckId(commandId);
            if (prefix.Any(c => c == ':' || char.IsWhiteSpace(c)))
                throw new ArgumentException("A prefix cannot contain a colon or whitespace.", nameof(prefix));
            if (!_owner.Prefixes.TryAdd(prefix, commandId))
                throw new InvalidOperationException($"Duplicate command prefix '{prefix}'.");
        }

        public void RegisterPanelProvider(IPanelProvider provider)
        {
            _owner.CheckWritable();
            ArgumentNullException.ThrowIfNull(provider);
            CheckId(provider.Id);
            if (!_owner.Providers.TryAdd(provider.Id, provider))
                throw new InvalidOperationException($"Duplicate panel provider '{provider.Id}'.");
        }

        private static void CheckId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A non-empty plugin-owned ID is required.");
        }
    }

    private sealed record ExecutionContext(
        IPluginApplicationContext Application, IPluginSettingsService Settings, IPluginUiService Ui,
        IPluginCredentialService Credentials, PanelSide TargetPanelSide, PluginCommandOrigin Origin,
        string? RawCommandLine, string? Arguments) : IPluginCommandContext;

    private sealed record ProviderContext(
        IPluginApplicationContext Application, IPluginSettingsService Settings, IPluginUiService Ui,
        IPluginCredentialService Credentials, PanelSide TargetPanelSide) : IPanelProviderContext;

    private sealed class ScopedSettings(string pluginId, string? demoDirectory, IPluginSettingsService? inner)
        : IPluginSettingsService
    {
        public string GetSettingsDirectory(string requestedPluginId)
        {
            if (!string.Equals(pluginId, requestedPluginId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("A plugin cannot access another plugin's settings.");
            if (demoDirectory is not null)
            {
                Directory.CreateDirectory(demoDirectory);
                return demoDirectory;
            }
            return (inner ?? throw new InvalidOperationException("Plugin settings are unavailable."))
                .GetSettingsDirectory(pluginId);
        }
    }

    private sealed class DeniedCredentials : IPluginCredentialService
    {
        public string? TryReadPassword(string credentialId) => throw Denied();
        public void SavePassword(string credentialId, string password) => throw Denied();
        public void DeletePassword(string credentialId) => throw Denied();
        private static UnauthorizedAccessException Denied() =>
            new("Credential access is disabled in demo mode.");
    }
}
