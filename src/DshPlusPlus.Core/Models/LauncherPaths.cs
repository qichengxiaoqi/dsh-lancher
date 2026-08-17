namespace DshPlusPlus.Core.Models;

public sealed record LauncherPaths
{
    public string DshRoot { get; init; } = string.Empty;
    public string DshHome { get; init; } = string.Empty;
    public string ProfileName { get; init; } = "web";
    public string ProfileDirectory { get; init; } = string.Empty;
    public string PluginRoot { get; init; } = string.Empty;
    public string ServiceScript { get; init; } = string.Empty;
    public string WebUrl { get; init; } = "http://127.0.0.1:3080";
    public int Port { get; init; } = 3080;
    public string PnpmStore { get; init; } = string.Empty;
    public string PowerShellPath { get; init; } = string.Empty;
    public string GitExecutable { get; init; } = "git.exe";
    public string PnpmExecutable { get; init; } = "pnpm.cmd";

    public string CredentialsFile => Path.Combine(DshHome, ".credentials.yaml");

    public string SettingsFile => Path.Combine(DshHome, "settings.yaml");

    public string ProfilePatchFile => Path.Combine(ProfileDirectory, "cordis.patch.yml");

    public static LauncherPaths CreateDefault() => new();

    public DshPaths ToDshPaths() => new(
        Root: DshRoot,
        ServiceScript: ServiceScript,
        WebUrl: WebUrl,
        Port: Port,
        PnpmStore: PnpmStore,
        PowerShellPath: PowerShellPath,
        GitExecutable: GitExecutable,
        PnpmExecutable: PnpmExecutable);
}
