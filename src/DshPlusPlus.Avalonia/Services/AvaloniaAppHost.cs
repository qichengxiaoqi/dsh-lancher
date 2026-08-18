using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.Avalonia.ViewModels;

namespace DshPlusPlus.Avalonia.Services;

public sealed class AvaloniaAppHost : IDisposable
{
    private readonly HttpClient _httpClient = new();

    public AvaloniaAppHost()
    {
        PathDiscovery = new LauncherPathDiscovery();
        SettingsStore = new LauncherSettingsStore(pathDiscovery: PathDiscovery);
        Settings = SettingsStore.Load();
        Paths = Settings.Paths.ToDshPaths();
        Runner = new ProcessRunner();
        ServiceController = new DshServiceController(Runner, Paths);
        StatusProbe = new ServiceStatusProbe(Paths, _httpClient);
        GitRepository = new GitRepositoryService(Paths, Runner, Settings.DshUpdates);
        CredentialStore = new DshCredentialStore();
        ApiClient = new DeepSeekApiClient();
        RuntimeInventory = new RuntimePluginInventoryClient();
        PluginInventory = new PluginInventoryService(Settings.Paths, RuntimeInventory);
        InstructionScanner = new SystemInstructionScanner(Settings.Paths);
        PatchService = new ProfilePatchService();
        LauncherUpdateService = new LauncherUpdateService(_httpClient);
        MainWindow = new MainWindowViewModel(this);
    }

    public LauncherPathDiscovery PathDiscovery { get; }
    public LauncherSettingsStore SettingsStore { get; }
    public LauncherSettings Settings { get; private set; }
    public DshPaths Paths { get; private set; }
    public IProcessRunner Runner { get; }
    public IDshServiceController ServiceController { get; }
    public ServiceStatusProbe StatusProbe { get; }
    public IGitRepositoryService GitRepository { get; }
    public DshCredentialStore CredentialStore { get; }
    public DeepSeekApiClient ApiClient { get; }
    public RuntimePluginInventoryClient RuntimeInventory { get; }
    public PluginInventoryService PluginInventory { get; }
    public SystemInstructionScanner InstructionScanner { get; }
    public ProfilePatchService PatchService { get; }
    public LauncherUpdateService LauncherUpdateService { get; }
    public MainWindowViewModel MainWindow { get; }

    public async Task SaveSettingsAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        await SettingsStore.SaveAsync(settings, cancellationToken);
        Settings = settings;
        Paths = settings.Paths.ToDshPaths();
    }

    public void Dispose()
    {
        MainWindow.Dispose();
        ApiClient.Dispose();
        RuntimeInventory.Dispose();
        _httpClient.Dispose();
    }
}
