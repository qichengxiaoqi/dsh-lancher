using System.Text.Json;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class LauncherSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly LauncherPathDiscovery _pathDiscovery;

    public LauncherSettingsStore(
        string? filePath = null,
        LauncherPathDiscovery? pathDiscovery = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dsh++",
            "settings.json");
        _pathDiscovery = pathDiscovery ?? new LauncherPathDiscovery();
    }

    public string FilePath => _filePath;

    public LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return ApplyDiscovery(LauncherSettings.CreateDefault());

            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(json, JsonOptions)
                           ?? LauncherSettings.CreateDefault();
            return ApplyDiscovery(settings);
        }
        catch (JsonException)
        {
            return ApplyDiscovery(LauncherSettings.CreateDefault());
        }
        catch (IOException)
        {
            return ApplyDiscovery(LauncherSettings.CreateDefault());
        }
    }

    private LauncherSettings ApplyDiscovery(LauncherSettings settings)
    {
        settings = Normalize(settings);
        if (!settings.AutoDetectPaths)
            return settings with
            {
                SchemaVersion = Math.Max(LauncherSettings.CurrentSchemaVersion, settings.SchemaVersion)
            };

        return settings with
        {
            SchemaVersion = Math.Max(LauncherSettings.CurrentSchemaVersion, settings.SchemaVersion),
            Paths = _pathDiscovery.Discover()
        };
    }

    private static LauncherSettings Normalize(LauncherSettings settings)
    {
        var updates = settings.DshUpdates;
        if (updates is null
            || string.IsNullOrWhiteSpace(updates.UpstreamRemoteName)
            || updates.UpstreamRemoteName.Any(char.IsWhiteSpace)
            || !DshPatchQueueService.IsValidBranchName(updates.PatchBranchName))
        {
            updates = new DshUpdateSettings();
        }

        return settings with
        {
            DshUpdates = updates,
            SkillImport = settings.SkillImport ?? new SkillImportSettings()
        };
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)
                        ?? throw new InvalidOperationException("设置文件目录无效。");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporaryPath = _filePath + ".tmp-" + Guid.NewGuid().ToString("N");

        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        try
        {
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
