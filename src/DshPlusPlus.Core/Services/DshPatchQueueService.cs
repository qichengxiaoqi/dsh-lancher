using System.Text.Json;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class DshPatchQueueService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);

    private readonly DshPaths _paths;
    private readonly IProcessRunner _processRunner;
    private readonly DshUpdateSettings _settings;
    private readonly DshUpdateLayout _layout;

    public DshPatchQueueService(
        DshPaths paths,
        IProcessRunner processRunner,
        DshUpdateSettings? settings = null,
        DshUpdateLayout? layout = null)
    {
        _paths = paths;
        _processRunner = processRunner;
        _settings = settings ?? new DshUpdateSettings();
        _layout = layout ?? DshUpdateLayout.CreateDefault();
        ValidateBranchName(_settings.PatchBranchName);
    }

    public DshUpdateLayout Layout => _layout;

    public void EnsureStorage()
    {
        Directory.CreateDirectory(_layout.PatchDirectory);
        Directory.CreateDirectory(_layout.WorkspaceDirectory);
    }

    public DshCompatibilityManifest? LoadCompatibility()
    {
        try
        {
            if (!File.Exists(_layout.CompatibilityFile))
                return null;

            return JsonSerializer.Deserialize<DshCompatibilityManifest>(
                File.ReadAllText(_layout.CompatibilityFile),
                JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    public async Task SaveCompatibilityAsync(
        DshCompatibilityManifest manifest,
        CancellationToken cancellationToken)
    {
        EnsureStorage();
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var temporaryPath = _layout.CompatibilityFile + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        try
        {
            File.Move(temporaryPath, _layout.CompatibilityFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<DshPatchQueueSnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        EnsureStorage();

        var branchResult = await RunGitAsync(["branch", "--show-current"], cancellationToken);
        var activeBranch = branchResult.Succeeded
            ? branchResult.StandardOutput.Trim()
            : string.Empty;
        var branchExists = (await RunGitAsync(
            ["show-ref", "--verify", "--quiet", $"refs/heads/{_settings.PatchBranchName}"],
            cancellationToken)).Succeeded;

        var commits = branchExists
            ? await ReadPatchCommitsAsync(cancellationToken)
            : [];

        return new DshPatchQueueSnapshot(
            _settings.PatchBranchName,
            branchExists,
            string.Equals(activeBranch, _settings.PatchBranchName, StringComparison.OrdinalIgnoreCase),
            commits.Count,
            _layout.PatchDirectory,
            _layout.WorkspaceDirectory,
            commits);
    }

    private async Task<IReadOnlyList<DshPatchCommit>> ReadPatchCommitsAsync(
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            ["log", "--format=%H%x09%s", _settings.PatchBranchName, "--max-count=100"],
            cancellationToken);
        if (!result.Succeeded)
            return [];

        return result.StandardOutput
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0)
            .Select(parts => new DshPatchCommit(parts[0], parts[1]))
            .ToArray();
    }

    private Task<ProcessResult> RunGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        _processRunner.RunAsync(
            _paths.GitExecutable,
            arguments,
            _paths.Root,
            GitTimeout,
            cancellationToken);

    private static void ValidateBranchName(string value)
    {
        if (!IsValidBranchName(value))
            throw new ArgumentException("补丁分支名称无效。", nameof(value));
    }

    public static bool IsValidBranchName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 200
        && !value.StartsWith('-')
        && !value.Contains("..", StringComparison.Ordinal)
        && !value.Any(char.IsWhiteSpace)
        && !value.Any(character => character is '~' or '^' or ':' or '?' or '*' or '[' or '\\');
}
