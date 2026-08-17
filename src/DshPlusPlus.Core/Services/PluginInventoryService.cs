using System.Text.Json;
using System.Text.RegularExpressions;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class PluginInventoryService
{
    private LauncherPaths _paths;
    private readonly RuntimePluginInventoryClient _runtimeClient;

    public PluginInventoryService(LauncherPaths paths, RuntimePluginInventoryClient runtimeClient)
    {
        _paths = paths;
        _runtimeClient = runtimeClient;
    }

    public void UpdatePaths(LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Volatile.Write(ref _paths, paths);
    }

    public async Task<IReadOnlyList<PluginInfo>> ScanAsync(CancellationToken cancellationToken)
    {
        var plugins = new List<PluginInfo>();
        var profileManifest = Path.Combine(_paths.ProfileDirectory, "package.json");
        if (File.Exists(profileManifest))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(profileManifest, cancellationToken));
            var root = document.RootElement;
            foreach (var name in ReadProfilePackages(root))
            {
                var packagePath = ResolvePackagePath(name, root);
                plugins.Add(ReadPlugin(name, packagePath));
            }
        }

        IReadOnlyList<RuntimePluginEntry> runtime = [];
        try
        {
            runtime = await _runtimeClient.ListAsync(_paths.WebUrl, cancellationToken);
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        var merged = plugins.Select(plugin =>
        {
            var runtimeEntry = runtime.FirstOrDefault(entry =>
                string.Equals(entry.ModuleName, plugin.ModuleName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.ModuleName, plugin.Name, StringComparison.OrdinalIgnoreCase));
            return runtimeEntry is null
                ? plugin
                : plugin with
                {
                    Enabled = runtimeEntry.Enabled,
                    FiberPhase = runtimeEntry.FiberPhase,
                    RuntimeAvailable = true,
                    EntryId = runtimeEntry.EntryId
                };
        }).ToList();

        foreach (var entry in runtime.Where(entry => !merged.Any(plugin =>
                     string.Equals(plugin.ModuleName, entry.ModuleName, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(plugin.Name, entry.ModuleName, StringComparison.OrdinalIgnoreCase))))
        {
            merged.Add(new PluginInfo(
                entry.ModuleName,
                string.Empty,
                "运行时发现的插件",
                entry.ModuleName,
                string.Empty,
                PluginSourceKind.RuntimeOnly,
                _paths.ProfileName,
                entry.Enabled,
                entry.FiberPhase,
                true,
                entry.EntryId,
                null));
        }

        return merged;
    }

    private IEnumerable<string> ReadProfilePackages(JsonElement root)
    {
        var names = new List<string>();
        if (root.TryGetProperty("dependencies", out var dependencies)
            && dependencies.ValueKind == JsonValueKind.Object)
            names.AddRange(dependencies.EnumerateObject().Select(property => property.Name));
        if (root.TryGetProperty("dsh", out var dsh)
            && dsh.TryGetProperty("profile", out var profile)
            && profile.TryGetProperty("bundles", out var bundles)
            && bundles.ValueKind == JsonValueKind.Array)
            names.AddRange(bundles.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!));
        return names.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private string? ResolvePackagePath(string name, JsonElement profileRoot)
    {
        if (profileRoot.TryGetProperty("dependencies", out var dependencies)
            && dependencies.TryGetProperty(name, out var version)
            && version.ValueKind == JsonValueKind.String
            && version.GetString()!.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var relative = version.GetString()![5..];
            return Path.GetFullPath(Path.Combine(_paths.ProfileDirectory, relative));
        }

        var candidates = new[]
        {
            Path.Combine(_paths.ProfileDirectory, "node_modules", name),
            Path.Combine(_paths.DshRoot, "node_modules", name)
        };
        var direct = candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "package.json")));
        if (direct is not null)
            return direct;

        if (Directory.Exists(Path.Combine(_paths.DshRoot, "packages")))
        {
            try
            {
                return Directory.EnumerateFiles(Path.Combine(_paths.DshRoot, "packages"), "package.json", SearchOption.AllDirectories)
                    .FirstOrDefault(path => PackageNameEquals(path, name)) is { } packageJson
                    ? Path.GetDirectoryName(packageJson)
                    : null;
            }
            catch (IOException)
            {
            }
        }
        return null;
    }

    private PluginInfo ReadPlugin(string name, string? packagePath)
    {
        var packageJson = packagePath is null ? null : Path.Combine(packagePath, "package.json");
        string version = "未知";
        string description = "未找到 package.json";
        if (packageJson is not null && File.Exists(packageJson))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
                version = document.RootElement.TryGetProperty("version", out var versionNode)
                    ? versionNode.GetString() ?? version
                    : version;
                description = document.RootElement.TryGetProperty("description", out var descriptionNode)
                    ? descriptionNode.GetString() ?? description
                    : description;
            }
            catch (JsonException)
            {
                description = "package.json 解析失败";
            }
        }

        var configId = packagePath is null ? null : FindConfigId(packagePath, name);
        return new PluginInfo(
            name,
            version,
            description,
            name,
            packagePath ?? string.Empty,
            Classify(name, packagePath, _paths),
            _paths.ProfileName,
            ReadDisabled(configId),
            null,
            false,
            null,
            configId);
    }

    private bool? ReadDisabled(string? configId)
    {
        if (configId is null)
            return null;
        foreach (var file in new[]
        {
            Path.Combine(_paths.DshHome, "cordis.patch.yml"),
            _paths.ProfilePatchFile
        })
        {
            if (!File.Exists(file))
                continue;
            var text = File.ReadAllText(file);
            var match = Regex.Match(text, $"(?ms)(?:^|\\n)\\s*-\\s*id:\\s*{Regex.Escape(configId)}\\b.*?(?=\\n\\s*-\\s*id:|\\z)");
            if (match.Success && Regex.IsMatch(match.Value, @"disabled\s*:\s*true", RegexOptions.IgnoreCase))
                return false;
        }
        return true;
    }

    private static string? FindConfigId(string packagePath, string name)
    {
        var patch = Path.Combine(packagePath, "cordis.patch.yml");
        if (!File.Exists(patch))
            return name;
        var text = File.ReadAllText(patch);
        var match = Regex.Match(text, $"(?ms)id:\\s*([^\\s#]+).*?name:\\s*['\\\"]?{Regex.Escape(name)}['\\\"]?");
        return match.Success ? match.Groups[1].Value.Trim('"', '\'') : name;
    }

    private static PluginSourceKind Classify(string name, string? path, LauncherPaths paths)
    {
        if (name.StartsWith("@deepseek-ai/", StringComparison.OrdinalIgnoreCase))
            return PluginSourceKind.OfficialBundle;
        if (!string.IsNullOrWhiteSpace(paths.PluginRoot)
            && path is not null
            && IsWithin(path, paths.PluginRoot))
            return PluginSourceKind.LocalPlugin;
        return path is null ? PluginSourceKind.Unknown : PluginSourceKind.ThirdParty;
    }

    private static bool IsWithin(string path, string parent)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedPath, normalizedParent.TrimEnd(Path.DirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool PackageNameEquals(string packageJson, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
            return document.RootElement.TryGetProperty("name", out var value)
                   && string.Equals(value.GetString(), name, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) when (ExceptionFilter())
        {
            return false;
        }
    }

    private static bool ExceptionFilter() => true;
}
