using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class PathValidator
{
    public PathValidationResult Validate(LauncherPaths paths)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!Directory.Exists(paths.DshRoot))
            errors.Add($"DSH 根目录不存在：{paths.DshRoot}");
        else
        {
            if (!File.Exists(Path.Combine(paths.DshRoot, "package.json")))
                errors.Add("DSH 根目录缺少 package.json。");
            if (!Directory.Exists(Path.Combine(paths.DshRoot, ".git"))
                && !File.Exists(Path.Combine(paths.DshRoot, ".git")))
                errors.Add("DSH 根目录缺少 .git。");
        }

        if (!File.Exists(paths.ServiceScript))
            errors.Add($"服务脚本不存在：{paths.ServiceScript}");
        if (!Directory.Exists(paths.ProfileDirectory))
            errors.Add($"Profile 目录不存在：{paths.ProfileDirectory}");
        else if (!File.Exists(Path.Combine(paths.ProfileDirectory, "package.json")))
            errors.Add("Profile 目录缺少 package.json。");
        if (!Directory.Exists(paths.PluginRoot))
            warnings.Add($"插件目录不存在，扫描时将显示为空：{paths.PluginRoot}");
        if (paths.Port is < 1 or > 65535)
            errors.Add("Web 端口必须在 1 到 65535 之间。");
        if (!Uri.TryCreate(paths.WebUrl, UriKind.Absolute, out var webUri)
            || webUri.Scheme is not ("http" or "https"))
            errors.Add("Web URL 必须是 http 或 https 地址。");

        CheckExecutable(paths.PowerShellPath, "PowerShell", errors, warnings);
        CheckExecutable(paths.GitExecutable, "Git", errors, warnings);
        CheckExecutable(paths.PnpmExecutable, "pnpm", errors, warnings);

        return new PathValidationResult(errors.Count == 0, errors, warnings);
    }

    private static void CheckExecutable(
        string executable,
        string displayName,
        ICollection<string> errors,
        ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            warnings.Add($"未检测到 {displayName}，启动时仍会尝试使用系统 PATH。");
            return;
        }

        if (Path.IsPathFullyQualified(executable))
        {
            if (!File.Exists(executable))
                errors.Add($"{displayName} 可执行文件不存在：{executable}");
            return;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var found = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executable))
            .Any(File.Exists);
        if (!found)
            warnings.Add($"未在 PATH 中找到 {displayName}，启动时仍会尝试调用：{executable}");
    }
}
