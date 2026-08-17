using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;

namespace DshPlusPlus;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var singleInstance = new Mutex(true, "Local\\dsh-plus-plus-launcher", out var createdNew);
        if (!createdNew)
            return;

        var pathDiscovery = new LauncherPathDiscovery();
        var settingsStore = new LauncherSettingsStore(pathDiscovery: pathDiscovery);
        var settings = settingsStore.Load();
        var paths = settings.Paths.ToDshPaths();
        var runner = new ProcessRunner();
        var serviceController = new DshServiceController(runner, paths);
        var statusProbe = new ServiceStatusProbe(paths);
        var gitRepository = new GitRepositoryService(paths, runner);
        var projectCommands = new ProjectCommandService(paths, runner);
        var updateCoordinator = new UpdateCoordinator(
            gitRepository,
            projectCommands,
            serviceController);
        var credentialStore = new DshCredentialStore();
        var apiClient = new DeepSeekApiClient();
        var runtimeInventory = new RuntimePluginInventoryClient();
        var pluginInventory = new PluginInventoryService(settings.Paths, runtimeInventory);
        var instructionScanner = new SystemInstructionScanner(settings.Paths);
        var patchService = new ProfilePatchService();
        using var updateHttpClient = new HttpClient();
        var launcherUpdateService = new LauncherUpdateService(updateHttpClient);

        using (apiClient)
        using (runtimeInventory)
        {
            Application.Run(new MainForm(
                settings,
                settingsStore,
                paths,
                serviceController,
                statusProbe,
                gitRepository,
                updateCoordinator,
                credentialStore,
                apiClient,
                instructionScanner,
                pluginInventory,
                patchService,
                pathDiscovery,
                launcherUpdateService));
        }
    }
}
