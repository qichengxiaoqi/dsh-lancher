using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed record SkillPathSet(
    string Codex,
    string ClaudeCode,
    string DshTarget);

public sealed class SkillPathResolver
{
    private readonly Func<string, string?> _environmentReader;

    public SkillPathResolver(Func<string, string?>? environmentReader = null)
    {
        _environmentReader = environmentReader ?? Environment.GetEnvironmentVariable;
    }

    public SkillPathSet Resolve(LauncherPaths launcherPaths, SkillImportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(launcherPaths);
        ArgumentNullException.ThrowIfNull(settings);

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var codex = Prefer(
            settings.CodexSkillsDirectory,
            AppendSkills(_environmentReader("CODEX_HOME")),
            AppendSkills(Path.Combine(profile, ".codex")));
        var claude = Prefer(
            settings.ClaudeSkillsDirectory,
            AppendSkills(_environmentReader("CLAUDE_CONFIG_DIR")),
            AppendSkills(Path.Combine(profile, ".claude")));
        var dshHome = string.IsNullOrWhiteSpace(launcherPaths.DshHome)
            ? Path.Combine(profile, ".dsh")
            : launcherPaths.DshHome;
        var dsh = Prefer(settings.DshSkillsDirectory, AppendSkills(dshHome));

        return new(
            Normalize(codex),
            Normalize(claude),
            Normalize(dsh));
    }

    private static string Prefer(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ?? string.Empty;

    private static string AppendSkills(string? root) =>
        string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, "skills");

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (NotSupportedException)
        {
            return string.Empty;
        }
    }
}
