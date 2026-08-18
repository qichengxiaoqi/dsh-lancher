using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

/// <summary>
/// Guards skill imports from DSH runtime and user-data locations.
/// Skill migration may copy a selected skill bundle only; it must never be
/// able to target the DSH home or carry session/configuration artifacts.
/// </summary>
public static class SkillMigrationSafety
{
    private static readonly string[] ReservedDshDirectories =
    [
        "sessions",
        "session-format-backups",
        "profiles"
    ];

    private static readonly HashSet<string> ReservedRuntimeNames = new(
        [
            "sessions",
            "session-format-backups",
            "profiles",
            "session.jsonl",
            "session.jsonl.zstd",
            ".credentials.yaml",
            "settings.yaml",
            "cordis.patch.yml",
            "cordis.yml"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static bool ValidateTargetRoot(
        SkillPathSet paths,
        out string error)
    {
        try
        {
            var target = Path.GetFullPath(paths.DshTarget)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(paths.DshHome))
            {
                error = string.Empty;
                return true;
            }

            var dshHome = Path.GetFullPath(paths.DshHome)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(target, dshHome, StringComparison.OrdinalIgnoreCase))
            {
                error = "技能目标不能是 DSH 根目录，只能是独立的 skills 子目录";
                return false;
            }

            foreach (var reservedName in ReservedDshDirectories)
            {
                var reservedPath = Path.Combine(dshHome, reservedName);
                if (SkillContentHasher.IsWithin(target, reservedPath))
                {
                    error = $"技能目标不能位于 DSH 运行时目录：{reservedName}";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
        catch (ArgumentException)
        {
            error = "DSH 技能目录路径无效";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "DSH 技能目录路径格式不受支持";
            return false;
        }
    }

    public static bool ValidateBundle(
        string sourcePath,
        bool isDirectoryBundle,
        CancellationToken cancellationToken,
        out string error)
    {
        try
        {
            if (!isDirectoryBundle)
            {
                if (IsReservedRuntimeName(Path.GetFileName(sourcePath)))
                {
                    error = "技能包包含 DSH 会话或运行时配置文件，已拒绝导入";
                    return false;
                }

                error = string.Empty;
                return true;
            }

            foreach (var file in SkillContentHasher.EnumerateRegularFiles(sourcePath, cancellationToken))
            {
                var relative = Path.GetRelativePath(sourcePath, file);
                var segments = relative.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries);
                if (segments.Any(IsReservedRuntimeName))
                {
                    error = "技能包包含 DSH 会话或运行时配置文件，已拒绝导入";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            error = exception.Message;
            return false;
        }
        catch (IOException exception)
        {
            error = $"无法检查技能包：{exception.Message}";
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = $"无法检查技能包：{exception.Message}";
            return false;
        }
    }

    private static bool IsReservedRuntimeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && ReservedRuntimeNames.Contains(name);
}
