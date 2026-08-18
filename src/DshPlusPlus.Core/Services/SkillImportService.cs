using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class SkillImportService
{
    private readonly string _backupRoot;
    private SkillPathSet _paths;

    public SkillImportService(SkillPathSet paths, string backupRoot)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _backupRoot = Path.GetFullPath(backupRoot);
    }

    public void UpdatePaths(SkillPathSet paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Volatile.Write(ref _paths, paths);
    }

    public static bool IsSelectable(SkillInfo skill) =>
        skill.State is SkillImportState.New or SkillImportState.Conflict;

    public Task<SkillImportResult> ImportAsync(
        SkillInfo skill,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skill);
        var paths = Volatile.Read(ref _paths);
        return Task.Run(() => ImportCore(skill, paths, cancellationToken), cancellationToken);
    }

    private SkillImportResult ImportCore(
        SkillInfo skill,
        SkillPathSet paths,
        CancellationToken cancellationToken)
    {
        if (skill.State is SkillImportState.Invalid or SkillImportState.Unsupported or SkillImportState.Error)
            return Failure("技能条目无效或不受支持");

        if (!TryValidatePaths(skill, paths, out var pathError))
            return Failure(pathError);

        var stagePath = skill.TargetPath + ".dsh++-stage-" + Guid.NewGuid().ToString("N");
        var oldPath = skill.TargetPath + ".dsh++-old-" + Guid.NewGuid().ToString("N");
        string? backupPath = null;
        var targetExists = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceHash = SkillContentHasher.Compute(
                skill.SourcePath, skill.IsDirectoryBundle, cancellationToken);
            targetExists = File.Exists(skill.TargetPath) || Directory.Exists(skill.TargetPath);
            if (targetExists)
            {
                if (SkillContentHasher.IsReparsePoint(skill.TargetPath))
                    return Failure("目标是链接，拒绝覆盖");
                var targetMatchesType = skill.IsDirectoryBundle
                    ? Directory.Exists(skill.TargetPath)
                    : File.Exists(skill.TargetPath);
                if (!targetMatchesType)
                    return Failure("目标已存在但类型不一致，未覆盖");

                var targetHash = SkillContentHasher.Compute(
                    skill.TargetPath, skill.IsDirectoryBundle, cancellationToken);
                if (string.Equals(sourceHash, targetHash, StringComparison.Ordinal))
                    return new SkillImportResult(true, "内容相同，已跳过", null, false);
            }

            CopyEntry(skill.SourcePath, stagePath, skill.IsDirectoryBundle, cancellationToken);
            var copiedHash = SkillContentHasher.Compute(
                stagePath, skill.IsDirectoryBundle, cancellationToken);
            if (!string.Equals(sourceHash, copiedHash, StringComparison.Ordinal))
                throw new InvalidDataException("来源在复制期间发生变化，未写入目标");

            Directory.CreateDirectory(Path.GetDirectoryName(skill.TargetPath)!);
            if (targetExists)
            {
                backupPath = CreateBackupPath(skill.Name, skill.IsDirectoryBundle);
                CopyEntry(skill.TargetPath, backupPath, skill.IsDirectoryBundle, cancellationToken);
                MoveEntry(skill.TargetPath, oldPath, skill.IsDirectoryBundle);
            }

            MoveEntry(stagePath, skill.TargetPath, skill.IsDirectoryBundle);
            if (targetExists)
                DeleteEntry(oldPath, skill.IsDirectoryBundle);

            return new SkillImportResult(
                true,
                targetExists ? "技能已覆盖并创建备份" : "技能已导入",
                backupPath,
                true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(stagePath, skill.IsDirectoryBundle);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or NotSupportedException)
        {
            TryDelete(stagePath, skill.IsDirectoryBundle);
            if (targetExists && (File.Exists(oldPath) || Directory.Exists(oldPath)))
            {
                TryDelete(skill.TargetPath, skill.IsDirectoryBundle);
                TryMove(oldPath, skill.TargetPath, skill.IsDirectoryBundle);
            }
            return new SkillImportResult(false, $"技能导入失败：{ex.Message}", backupPath, false);
        }
    }

    private bool TryValidatePaths(SkillInfo skill, SkillPathSet paths, out string error)
    {
        try
        {
            var sourceRoot = skill.SourceKind switch
            {
                SkillSourceKind.Codex => paths.Codex,
                SkillSourceKind.ClaudeCode => paths.ClaudeCode,
                _ => string.Empty
            };
            if (string.IsNullOrWhiteSpace(sourceRoot)
                || !SkillContentHasher.IsWithin(skill.SourcePath, sourceRoot))
            {
                error = "来源路径不在已配置的技能目录内";
                return false;
            }
            if (!SkillMigrationSafety.ValidateTargetRoot(paths, out error))
                return false;
            if (string.IsNullOrWhiteSpace(paths.DshTarget)
                || !SkillContentHasher.IsWithin(skill.TargetPath, paths.DshTarget)
                || string.Equals(Path.GetFullPath(skill.TargetPath), Path.GetFullPath(paths.DshTarget),
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "目标路径不在 DSH 技能目录内";
                return false;
            }
            if (string.Equals(Path.GetFullPath(skill.SourcePath), Path.GetFullPath(skill.TargetPath),
                    StringComparison.OrdinalIgnoreCase)
                || SkillContentHasher.IsWithin(skill.TargetPath, skill.SourcePath)
                || SkillContentHasher.IsWithin(skill.SourcePath, skill.TargetPath))
            {
                error = "来源和目标不能相互嵌套";
                return false;
            }

            var sourceExists = skill.IsDirectoryBundle
                ? Directory.Exists(skill.SourcePath)
                : File.Exists(skill.SourcePath);
            if (!sourceExists || SkillContentHasher.IsReparsePoint(skill.SourcePath))
            {
                error = "来源不存在或是链接";
                return false;
            }
            if (!SkillMigrationSafety.ValidateBundle(
                    skill.SourcePath,
                    skill.IsDirectoryBundle,
                    CancellationToken.None,
                    out error))
            {
                return false;
            }
            error = string.Empty;
            return true;
        }
        catch (ArgumentException)
        {
            error = "来源或目标路径无效";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "来源或目标路径格式不受支持";
            return false;
        }
        catch (IOException exception)
        {
            error = $"无法检查来源或目标路径：{exception.Message}";
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = $"无法检查来源或目标路径：{exception.Message}";
            return false;
        }
    }

    private string CreateBackupPath(string name, bool isDirectoryBundle)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string((string.IsNullOrWhiteSpace(name) ? "skill" : name)
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray())
            .Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(safeName) || safeName is "." or "..")
            safeName = "skill";
        var directory = Path.Combine(_backupRoot, timestamp);
        var path = Path.Combine(directory, safeName + (isDirectoryBundle ? string.Empty : ".md"));
        var suffix = 0;
        while (File.Exists(path) || Directory.Exists(path))
        {
            suffix++;
            path = Path.Combine(directory, $"{safeName}-{suffix}{(isDirectoryBundle ? string.Empty : ".md")}");
        }
        Directory.CreateDirectory(directory);
        return path;
    }

    private static void CopyEntry(
        string source,
        string destination,
        bool isDirectoryBundle,
        CancellationToken cancellationToken)
    {
        if (isDirectoryBundle)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in SkillContentHasher.EnumerateRegularFiles(source, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
    }

    private static void MoveEntry(string source, string destination, bool isDirectoryBundle)
    {
        if (isDirectoryBundle)
            Directory.Move(source, destination);
        else
            File.Move(source, destination);
    }

    private static void TryMove(string source, string destination, bool isDirectoryBundle)
    {
        try
        {
            if ((isDirectoryBundle && Directory.Exists(source)) || (!isDirectoryBundle && File.Exists(source)))
                MoveEntry(source, destination, isDirectoryBundle);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteEntry(string path, bool isDirectoryBundle)
    {
        if (isDirectoryBundle)
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }

    private static void TryDelete(string path, bool isDirectoryBundle)
    {
        try
        {
            if ((isDirectoryBundle && Directory.Exists(path)) || (!isDirectoryBundle && File.Exists(path)))
                DeleteEntry(path, isDirectoryBundle);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static SkillImportResult Failure(string message) =>
        new(false, message, null, false);
}
