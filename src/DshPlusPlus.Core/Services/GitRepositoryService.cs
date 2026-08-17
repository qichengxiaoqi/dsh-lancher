using System.Text.Json;
using System.Text.RegularExpressions;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public interface IGitRepositoryService
{
    Task<RepositorySnapshot> ReadLocalSnapshotAsync(CancellationToken cancellationToken);
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);
    Task<ProcessResult> PullFastForwardOnlyAsync(
        string remoteRef,
        CancellationToken cancellationToken);
}

public sealed class GitRepositoryService : IGitRepositoryService
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan CompareTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PullTimeout = TimeSpan.FromSeconds(120);
    private static readonly Regex RemoteRefRegex = new(
        "^[A-Za-z0-9._-]+/[A-Za-z0-9._/-]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private readonly DshPaths _paths;
    private readonly IProcessRunner _processRunner;

    public GitRepositoryService(DshPaths paths, IProcessRunner processRunner)
    {
        _paths = paths;
        _processRunner = processRunner;
    }

    public async Task<RepositorySnapshot> ReadLocalSnapshotAsync(CancellationToken cancellationToken)
    {
        EnsureRepositoryFiles();

        var status = await RunRequiredAsync(
            ["-c", "core.quotepath=false", "status", "--porcelain=v1", "--untracked-files=all"],
            ReadTimeout,
            cancellationToken);
        var branch = await RunRequiredAsync(["branch", "--show-current"], ReadTimeout, cancellationToken);
        var head = await RunRequiredAsync(["rev-parse", "HEAD"], ReadTimeout, cancellationToken);
        var shortHead = await RunRequiredAsync(["rev-parse", "--short", "HEAD"], ReadTimeout, cancellationToken);
        var remote = await RunOptionalAsync(["remote", "get-url", "origin"], ReadTimeout, cancellationToken);
        var upstream = await RunOptionalAsync(
            ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"],
            ReadTimeout,
            cancellationToken);
        var localVersion = await ReadPackageVersionAsync(_paths.PackageJsonPath, cancellationToken);

        var statusPaths = ParseStatusPaths(status.StandardOutput);
        var localOnlyChanges = statusPaths
            .Where(change => change.IsUntracked && IsKnownLocalFile(change.Path))
            .Select(change => change.Path)
            .ToArray();
        var hasBlockingChanges = statusPaths.Any(change =>
            !change.IsUntracked || !IsKnownLocalFile(change.Path));

        return new RepositorySnapshot(
            Root: _paths.Root,
            Branch: branch.StandardOutput.Trim(),
            HeadSha: head.StandardOutput.Trim(),
            ShortSha: shortHead.StandardOutput.Trim(),
            LocalPackageVersion: localVersion,
            RemotePackageVersion: null,
            RemoteUrl: remote?.StandardOutput.Trim() ?? string.Empty,
            UpstreamRef: NullIfEmpty(upstream?.StandardOutput),
            ResolvedRemoteRef: null,
            Ahead: 0,
            Behind: 0,
            IsDirty: hasBlockingChanges,
            localOnlyChanges: localOnlyChanges);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            EnsureRepositoryFiles();
            var local = await ReadLocalSnapshotAsync(cancellationToken);
            if (!RemoteResolver.IsGitHubUrl(local.RemoteUrl))
            {
                return new UpdateCheckResult(
                    string.IsNullOrWhiteSpace(local.RemoteUrl)
                        ? UpdateState.InvalidRemote
                        : UpdateState.InvalidRemote,
                    string.IsNullOrWhiteSpace(local.RemoteUrl)
                        ? "未找到 origin 远程仓库。"
                        : "origin 不是受支持的 GitHub 地址。",
                    local);
            }

            var fetch = await RunGitAsync(
                ["fetch", "origin", "--prune"], FetchTimeout, cancellationToken);
            if (!fetch.Succeeded)
            {
                return new UpdateCheckResult(
                    UpdateState.CannotConnect,
                    $"fetch 失败：{Summarize(fetch)}",
                    local with { Error = Summarize(fetch) });
            }

            var remoteRef = await ResolveRemoteRefAsync(local.UpstreamRef, cancellationToken);
            if (remoteRef is null)
            {
                return new UpdateCheckResult(
                    UpdateState.NoUpstream,
                    "没有可比较的 upstream、origin/main 或 origin/master。",
                    local);
            }

            var counts = await RunRequiredAsync(
                ["rev-list", "--left-right", "--count", $"HEAD...{remoteRef}"],
                CompareTimeout,
                cancellationToken);
            var (ahead, behind) = ParseAheadBehind(counts.StandardOutput);
            var remoteShortSha = await RunRequiredAsync(
                ["rev-parse", "--short", remoteRef], CompareTimeout, cancellationToken);
            var remoteVersion = await ReadRemotePackageVersionAsync(remoteRef, cancellationToken);
            var snapshot = local with
            {
                ResolvedRemoteRef = remoteRef,
                Ahead = ahead,
                Behind = behind,
                RemotePackageVersion = remoteVersion,
                Error = null
            };

            var state = UpdateDecision.Evaluate(ahead, behind, local.IsDirty);
            return new UpdateCheckResult(
                state,
                BuildMessage(
                    state,
                    ahead,
                    behind,
                    remoteShortSha.StandardOutput.Trim(),
                    remoteVersion,
                    snapshot.LocalOnlyChanges),
                snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GitCommandException ex)
        {
            return new UpdateCheckResult(UpdateState.Error, ex.Message, ex.Snapshot);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateState.Error, $"检查更新失败：{ex.Message}");
        }
    }

    public Task<ProcessResult> PullFastForwardOnlyAsync(
        string remoteRef,
        CancellationToken cancellationToken)
    {
        if (!IsRemoteRef(remoteRef))
            throw new ArgumentException("远程 ref 格式无效。", nameof(remoteRef));

        var separator = remoteRef.IndexOf('/');
        var remote = remoteRef[..separator];
        var branch = remoteRef[(separator + 1)..];
        return RunGitAsync(
            ["pull", "--ff-only", remote, branch],
            PullTimeout,
            cancellationToken);
    }

    private async Task<string?> ResolveRemoteRefAsync(
        string? upstream,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { upstream, "origin/main", "origin/master" })
        {
            if (!IsRemoteRef(candidate))
                continue;

            var verification = await RunGitAsync(
                ["rev-parse", "--verify", $"refs/remotes/{candidate}^{{commit}}"],
                CompareTimeout,
                cancellationToken);
            if (verification.Succeeded)
                return candidate;
        }

        return null;
    }

    private async Task<string?> ReadRemotePackageVersionAsync(
        string remoteRef,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            ["show", $"{remoteRef}:package.json"], CompareTimeout, cancellationToken);
        return result.Succeeded ? ParsePackageVersion(result.StandardOutput) : null;
    }

    private async Task<string> ReadPackageVersionAsync(
        string packageJsonPath,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(packageJsonPath, cancellationToken);
        return ParsePackageVersion(json)
               ?? throw new InvalidDataException("package.json 缺少 version 字段。");
    }

    private static string? ParsePackageVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("version", out var version)
            ? version.GetString()
            : null;
    }

    private async Task<ProcessResult> RunRequiredAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(arguments, timeout, cancellationToken);
        if (!result.Succeeded)
            throw new GitCommandException($"Git 命令失败：{Summarize(result)}");
        return result;
    }

    private async Task<ProcessResult?> RunOptionalAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(arguments, timeout, cancellationToken);
        return result.Succeeded ? result : null;
    }

    private Task<ProcessResult> RunGitAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        _processRunner.RunAsync(
            _paths.GitExecutable,
            arguments,
            _paths.Root,
            timeout,
            cancellationToken);

    private void EnsureRepositoryFiles()
    {
        if (!Directory.Exists(_paths.Root))
            throw new DirectoryNotFoundException($"找不到 DSH 源码目录：{_paths.Root}");
        if (!Directory.Exists(_paths.GitDirectory) && !File.Exists(_paths.GitDirectory))
            throw new DirectoryNotFoundException($"找不到 Git 仓库：{_paths.GitDirectory}");
        if (!File.Exists(_paths.PackageJsonPath))
            throw new FileNotFoundException("找不到 package.json。", _paths.PackageJsonPath);
    }

    private static (int Ahead, int Behind) ParseAheadBehind(string output)
    {
        var parts = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out var ahead) || !int.TryParse(parts[1], out var behind))
            throw new InvalidDataException($"无法解析 Git ahead/behind 结果：{output.Trim()}");
        return (ahead, behind);
    }

    private static bool IsRemoteRef(string? value) =>
        value is not null && RemoteRefRegex.IsMatch(value);

    private bool IsKnownLocalFile(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_paths.Root, relativePath));
        return _paths.KnownLocalFiles.Any(path =>
            string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<GitStatusPath> ParseStatusPaths(string output) =>
        output.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length >= 3)
            .Select(line => new GitStatusPath(
                line[3..].Trim(),
                line.StartsWith("??", StringComparison.Ordinal)))
            .Where(change => change.Path.Length > 0)
            .ToArray();

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildMessage(
        UpdateState state,
        int ahead,
        int behind,
        string remoteShortSha,
        string? remoteVersion,
        IReadOnlyList<string> localOnlyChanges) => state switch
        {
            UpdateState.Latest => $"已是最新（远程 {remoteShortSha}，版本 {remoteVersion ?? "未知"}）。",
            UpdateState.UpdateAvailable when localOnlyChanges.Count > 0 =>
                $"发现 {behind} 个可用更新（远程 {remoteShortSha}，版本 {remoteVersion ?? "未知"}）；已识别 {localOnlyChanges.Count} 个本地脚本，更新时会保护。",
            UpdateState.UpdateAvailable => $"发现 {behind} 个可用更新（远程 {remoteShortSha}，版本 {remoteVersion ?? "未知"}）。",
            UpdateState.LocalAhead => $"本地领先远程 {ahead} 个提交；启动器不提供 push。",
            UpdateState.DirtyWorktree => "工作区有未提交或未跟踪修改，禁止拉取更新。",
            _ => $"远程状态：{state}。"
        };

    private sealed record GitStatusPath(string Path, bool IsUntracked);

    private static string Summarize(ProcessResult result)
    {
        var text = result.CombinedOutput;
        return text.Length <= 600 ? text : text[..600] + "…";
    }

    private sealed class GitCommandException : Exception
    {
        public GitCommandException(string message, RepositorySnapshot? snapshot = null)
            : base(message)
        {
            Snapshot = snapshot;
        }

        public RepositorySnapshot? Snapshot { get; }
    }
}
