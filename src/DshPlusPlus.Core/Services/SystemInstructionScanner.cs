using System.Diagnostics;
using System.Security.Cryptography;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class SystemInstructionScanner
{
    private static readonly string[] MarkdownNames =
    [
        "AGENTS.md",
        "CLAUDE.md",
        "AGENTS.local.md",
        "CLAUDE.local.md"
    ];

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".pnpm", ".pnpm-store", "node_modules",
        "bin", "obj", "dist", "build", "out", "coverage", "artifacts", "sessions"
    };

    private readonly LauncherPaths _paths;
    private readonly SystemInstructionScanOptions _options;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _cacheGate = new();
    private IReadOnlyList<SystemInstructionFileInfo>? _cachedFiles;
    private DateTimeOffset _cachedAt;

    public SystemInstructionScanner(
        LauncherPaths paths,
        SystemInstructionScanOptions? options = null)
    {
        _paths = paths;
        _options = options ?? new SystemInstructionScanOptions();
    }

    public Task<IReadOnlyList<SystemInstructionFileInfo>> ScanAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<SystemInstructionFileInfo>>(cancellationToken);
        return ScanAsyncCore(cancellationToken);
    }

    public void ClearCache()
    {
        lock (_cacheGate)
        {
            _cachedFiles = null;
            _cachedAt = default;
        }
    }

    private async Task<IReadOnlyList<SystemInstructionFileInfo>> ScanAsyncCore(
        CancellationToken cancellationToken)
    {
        lock (_cacheGate)
        {
            if (_cachedFiles is not null
                && DateTimeOffset.UtcNow - _cachedAt < _options.CacheDuration)
                return _cachedFiles;
        }

        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_cacheGate)
            {
                if (_cachedFiles is not null
                    && DateTimeOffset.UtcNow - _cachedAt < _options.CacheDuration)
                    return _cachedFiles;
            }

            var files = await RunScanOnLowPriorityThread(cancellationToken)
                .ConfigureAwait(false);
            lock (_cacheGate)
            {
                _cachedFiles = files;
                _cachedAt = DateTimeOffset.UtcNow;
            }
            return files;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private IReadOnlyList<SystemInstructionFileInfo> ScanCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var budget = new ScanBudget(_options, Stopwatch.StartNew());
        var candidates = new List<(string Path, string Scope, SystemInstructionKind Kind, string Note)>();
        AddIfExists(candidates, Path.Combine(_paths.DshHome, "AGENTS.md"), "global", SystemInstructionKind.MarkdownInstruction, "DSH 全局指令");
        AddProjectFilesBounded(candidates, _paths.DshRoot, cancellationToken, budget);
        AddIfExists(candidates, Path.Combine(_paths.DshHome, "settings.yaml"), "global", SystemInstructionKind.StructuredSettings, "DSH 结构化设置");
        AddIfExists(candidates, Path.Combine(_paths.DshHome, "cordis.patch.yml"), "home", SystemInstructionKind.ProfilePatch, "Home 插件配置覆盖");
        AddIfExists(candidates, Path.Combine(_paths.ProfileDirectory, "cordis.patch.yml"), "profile", SystemInstructionKind.ProfilePatch, "Profile 插件配置覆盖");
        AddIfExists(candidates, Path.Combine(_paths.DshRoot, ".claude", "skills"), "project", SystemInstructionKind.SkillLink, "Claude skills 兼容链接");

        var infos = new List<SystemInstructionFileInfo>();
        foreach (var candidate in candidates.DistinctBy(item => Path.GetFullPath(item.Path), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(candidate.Path);
                if (!info.Exists)
                    continue;
                infos.Add(new SystemInstructionFileInfo(
                    info.FullName,
                    candidate.Scope,
                    candidate.Kind,
                    info.Length,
                    info.LastWriteTime,
                    ComputeHash(info, cancellationToken),
                    false,
                    true,
                    candidate.Note));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var duplicateHashes = infos.GroupBy(info => info.Sha256, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var result = infos.Select(info => info with
        {
            IsDuplicate = duplicateHashes.Contains(info.Sha256),
            IsActive = IsActive(info, duplicateHashes.Contains(info.Sha256))
        }).ToArray();
        return result;
    }

    private Task<IReadOnlyList<SystemInstructionFileInfo>> RunScanOnLowPriorityThread(
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<SystemInstructionFileInfo>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(ScanCore(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "dsh++ instruction scan"
        };
        thread.Start();
        return completion.Task;
    }

    private static void AddProjectFilesBounded(
        ICollection<(string Path, string Scope, SystemInstructionKind Kind, string Note)> candidates,
        string root,
        CancellationToken cancellationToken,
        ScanBudget budget)
    {
        if (!Directory.Exists(root))
            return;

        var directories = new Stack<string>();
        directories.Push(root);
        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0
        };

        try
        {
            while (directories.Count > 0 && budget.CanContinue(cancellationToken))
            {
                var directory = directories.Pop();
                budget.DirectoriesVisited++;
                foreach (var file in Directory.EnumerateFiles(directory, "*", enumerationOptions))
                {
                    if (!budget.CanContinue(cancellationToken))
                        break;
                    var name = Path.GetFileName(file);
                    if (!MarkdownNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        continue;
                    budget.MatchedFiles++;
                    candidates.Add((file, "project", SystemInstructionKind.MarkdownInstruction,
                        name.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase)
                            ? "Claude Code instruction"
                            : "Project DSH instruction"));
                }

                foreach (var child in Directory.EnumerateDirectories(directory, "*", enumerationOptions))
                {
                    if (!budget.CanContinue(cancellationToken))
                        break;
                    if (IgnoredDirectoryNames.Contains(Path.GetFileName(child)))
                        continue;
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }
                    directories.Push(child);
                }
            }
        }
        catch (IOException)
        {
            budget.Stop("io-error");
        }
        catch (UnauthorizedAccessException)
        {
            budget.Stop("access-denied");
        }
    }

    private static void AddProjectFiles(
        ICollection<(string Path, string Scope, SystemInstructionKind Kind, string Note)> candidates,
        string root)
    {
        if (!Directory.Exists(root))
            return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (!MarkdownNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                candidates.Add((file, "project", SystemInstructionKind.MarkdownInstruction,
                    name.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase)
                        ? "Claude Code 兼容文件"
                        : "项目级 DSH 指令"));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void AddIfExists(
        ICollection<(string Path, string Scope, SystemInstructionKind Kind, string Note)> candidates,
        string path,
        string scope,
        SystemInstructionKind kind,
        string note)
    {
        if (File.Exists(path))
            candidates.Add((path, scope, kind, note));
    }

    private string ComputeHash(FileInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (info.Length > _options.MaxHashBytes)
            return $"size:{info.Length}";
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(info.FullName))).ToLowerInvariant();
    }

    private sealed class ScanBudget
    {
        private readonly SystemInstructionScanOptions _options;
        private readonly Stopwatch _stopwatch;

        public ScanBudget(SystemInstructionScanOptions options, Stopwatch stopwatch)
        {
            _options = options;
            _stopwatch = stopwatch;
        }

        public int DirectoriesVisited { get; set; }
        public int MatchedFiles { get; set; }
        public bool IsTruncated { get; private set; }
        public string StopReason { get; private set; } = "completed";

        public bool CanContinue(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DirectoriesVisited >= _options.MaxDirectories)
                return CheckAndStop("directory-limit");
            if (MatchedFiles >= _options.MaxMatchedFiles)
                return CheckAndStop("file-limit");
            if (_stopwatch.Elapsed >= _options.TimeBudget)
                return CheckAndStop("time-limit");
            return true;
        }

        public void Stop(string reason)
        {
            IsTruncated = true;
            StopReason = reason;
        }

        private bool CheckAndStop(string reason)
        {
            Stop(reason);
            return false;
        }
    }

    private static bool IsActive(SystemInstructionFileInfo info, bool duplicate)
    {
        if (!duplicate || info.Kind != SystemInstructionKind.MarkdownInstruction)
            return true;
        var name = Path.GetFileName(info.Path);
        return !name.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase);
    }
}
