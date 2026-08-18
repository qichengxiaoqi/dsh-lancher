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
        var gitRepository = new GitRepositoryService(paths, runner, settings.DshUpdates);
        var patchQueue = new DshPatchQueueService(paths, runner, settings.DshUpdates);
        var credentialStore = new DshCredentialStore();
        var apiClient = new DeepSeekApiClient();
        var runtimeInventory = new RuntimePluginInventoryClient();
        var pluginInventory = new PluginInventoryService(settings.Paths, runtimeInventory);
        var skillPathResolver = new SkillPathResolver();
        var skillPaths = skillPathResolver.Resolve(settings.Paths, settings.SkillImport);
        var skillInventory = new SkillInventoryService(ToSkillImportSettings(skillPaths));
        var skillImporter = new SkillImportService(
            skillPaths,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dsh++",
                "backups",
                "skills"));
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
                credentialStore,
                apiClient,
                instructionScanner,
                pluginInventory,
                skillPaths,
                skillInventory,
                skillImporter,
                patchService,
                pathDiscovery,
                launcherUpdateService,
                patchQueue));
        }
    }

    private static SkillImportSettings ToSkillImportSettings(SkillPathSet paths) => new()
    {
        CodexSkillsDirectory = paths.Codex,
        ClaudeSkillsDirectory = paths.ClaudeCode,
        DshSkillsDirectory = paths.DshTarget
    };
}
