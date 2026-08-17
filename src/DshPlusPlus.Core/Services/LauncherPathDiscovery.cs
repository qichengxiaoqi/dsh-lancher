using System.Text.Json;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class LauncherPathDiscovery
{
    private readonly string _applicationBaseDirectory;
    private readonly string _userProfileDirectory;
    private readonly Func<string, string?> _environment;

    public LauncherPathDiscovery(
        string? applicationBaseDirectory = null,
        string? userProfileDirectory = null,
        Func<string, string?>? environmentReader = null)
    {
        _applicationBaseDirectory = NormalizeDirectory(
            applicationBaseDirectory ?? AppContext.BaseDirectory);
        _userProfileDirectory = NormalizeDirectory(
            userProfileDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _environment = environmentReader ?? Environment.GetEnvironmentVariable;
    }

    public LauncherPaths Discover()
    {
        var dshRoot = FindDshRoot();
        var dshHome = FindDshHome();
        var profileName = ReadProfileName();
        var profileDirectory = FindProfileDirectory(dshHome, profileName);

        return new LauncherPaths
        {
            DshRoot = dshRoot,
            DshHome = dshHome,
            ProfileName = profileName,
            ProfileDirectory = profileDirectory,
            PluginRoot = FindPluginRoot(dshRoot, dshHome, profileDirectory),
            ServiceScript = FindServiceScript(dshRoot),
            WebUrl = "http://127.0.0.1:3080",
            Port = 3080,
            PnpmStore = FirstEnvironmentValue("PNPM_STORE_DIR", "NPM_CONFIG_STORE_DIR"),
            PowerShellPath = ResolveTool(
                ["DSH_POWERSHELL", "POWERSHELL_PATH"],
                ["powershell.exe", "pwsh.exe"]),
            GitExecutable = ResolveTool(["DSH_GIT", "GIT_EXECUTABLE"], ["git.exe"]),
            PnpmExecutable = ResolveTool(
                ["DSH_PNPM", "PNPM_EXECUTABLE"],
                ["pnpm.cmd", "pnpm.exe"])
        };
    }

    private string FindDshRoot()
    {
        foreach (var candidate in RootCandidates())
        {
            if (LooksLikeDshRoot(candidate))
                return candidate;
        }

        return string.Empty;
    }

    private IEnumerable<string> RootCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in new[]
                 {
                     FirstEnvironmentValue("DSH_ROOT", "DEEPSEEK_DSH_ROOT"),
                     _applicationBaseDirectory
                 })
        {
            if (!string.IsNullOrWhiteSpace(value))
                AddCandidate(seen, value);
        }

        var current = _applicationBaseDirectory;
        for (var depth = 0; depth < 6 && !string.IsNullOrWhiteSpace(current); depth++)
        {
            foreach (var name in new[]
                     { "Deepseek-dsh", "DeepSeek-dsh", "deepseek-harness" })
            {
                AddCandidate(seen, Path.Combine(current, name));
            }

            AddCandidate(seen, current);
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        return seen;
    }

    private string FindDshHome()
    {
        var configured = FirstEnvironmentValue("DSH_HOME", "DEEPSEEK_DSH_HOME");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(_userProfileDirectory, ".dsh")
            : NormalizeDirectory(configured);
    }

    private string ReadProfileName()
    {
        var configured = FirstEnvironmentValue("DSH_PROFILE_NAME");
        return string.IsNullOrWhiteSpace(configured)
               || !string.Equals(Path.GetFileName(configured), configured, StringComparison.Ordinal)
            ? "web"
            : configured.Trim();
    }

    private string FindProfileDirectory(string dshHome, string profileName)
    {
        var configured = FirstEnvironmentValue("DSH_PROFILE_DIR", "DSH_PROFILE");
        if (!string.IsNullOrWhiteSpace(configured))
            return NormalizeDirectory(configured);

        var profilesDirectory = Path.Combine(dshHome, "profiles");
        var preferred = Path.Combine(profilesDirectory, profileName);
        if (File.Exists(Path.Combine(preferred, "package.json")) || Directory.Exists(preferred))
            return preferred;

        if (Directory.Exists(profilesDirectory))
        {
            try
            {
                var discovered = Directory.EnumerateDirectories(profilesDirectory)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(path => File.Exists(Path.Combine(path, "package.json")));
                if (discovered is not null)
                    return discovered;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return preferred;
    }

    private string FindPluginRoot(string dshRoot, string dshHome, string profileDirectory)
    {
        var configured = FirstEnvironmentValue("DSH_PLUGIN_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
            return NormalizeDirectory(configured);

        if (!string.IsNullOrWhiteSpace(dshRoot))
        {
            var sibling = Path.Combine(Directory.GetParent(dshRoot)?.FullName ?? string.Empty, "dsp");
            if (Directory.Exists(sibling))
                return sibling;
        }

        var homePlugins = Path.Combine(dshHome, "plugins");
        if (Directory.Exists(homePlugins))
            return homePlugins;

        return FindFileDependencyRoot(profileDirectory);
    }

    private string FindServiceScript(string dshRoot)
    {
        var configured = FirstEnvironmentValue("DSH_SERVICE_SCRIPT");
        if (!string.IsNullOrWhiteSpace(configured))
            return NormalizeDirectory(configured);
        if (string.IsNullOrWhiteSpace(dshRoot))
            return string.Empty;

        var windowsScripts = Path.Combine(dshRoot, "scripts", "windows");
        var standard = Path.Combine(windowsScripts, "DeepSeekHarnessService.ps1");
        if (File.Exists(standard))
            return standard;

        if (!Directory.Exists(windowsScripts))
            return standard;

        try
        {
            return Directory.EnumerateFiles(windowsScripts, "*Harness*Service*.ps1")
                       .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                       .FirstOrDefault()
                   ?? standard;
        }
        catch (IOException)
        {
            return standard;
        }
        catch (UnauthorizedAccessException)
        {
            return standard;
        }
    }

    private string FindFileDependencyRoot(string profileDirectory)
    {
        var manifest = Path.Combine(profileDirectory, "package.json");
        if (!File.Exists(manifest))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            foreach (var sectionName in new[] { "dependencies", "devDependencies", "optionalDependencies" })
            {
                if (!document.RootElement.TryGetProperty(sectionName, out var section)
                    || section.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var property in section.EnumerateObject())
                {
                    var value = property.Value.GetString();
                    if (value is null || !value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var relative = value["file:".Length..].Trim();
                    var packagePath = Path.GetFullPath(Path.Combine(profileDirectory, relative));
                    if (File.Exists(packagePath))
                        packagePath = Path.GetDirectoryName(packagePath) ?? packagePath;
                    if (Directory.Exists(packagePath))
                        return Directory.GetParent(packagePath)?.FullName ?? packagePath;
                }
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return string.Empty;
    }

    private string ResolveTool(IReadOnlyList<string> variableNames, IReadOnlyList<string> candidates)
    {
        var configured = FirstEnvironmentValue(variableNames.ToArray());
        if (!string.IsNullOrWhiteSpace(configured))
            return NormalizeExecutable(configured);

        foreach (var candidate in candidates)
        {
            var resolved = FindOnPath(candidate);
            if (resolved is not null)
                return resolved;
        }

        return candidates[0];
    }

    private string? FindOnPath(string executable)
    {
        var path = _environment("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedDirectory = directory.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(normalizedDirectory))
                continue;

            var candidate = Path.Combine(normalizedDirectory, executable);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private string FirstEnvironmentValue(params string[] names)
    {
        foreach (var name in names)
        {
            var value = _environment(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static bool LooksLikeDshRoot(string path) =>
        Directory.Exists(path)
        && File.Exists(Path.Combine(path, "package.json"))
        && (Directory.Exists(Path.Combine(path, ".git"))
            || File.Exists(Path.Combine(path, ".git")));

    private static void AddCandidate(ISet<string> seen, string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            seen.Add(NormalizeDirectory(path));
    }

    private static string NormalizeDirectory(string path) => Path.GetFullPath(path.Trim());

    private static string NormalizeExecutable(string value) =>
        Path.IsPathFullyQualified(value) ? Path.GetFullPath(value.Trim()) : value.Trim();
}
