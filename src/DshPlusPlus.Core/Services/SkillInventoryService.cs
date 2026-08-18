using System.Text;
using System.Text.RegularExpressions;
using DshPlusPlus.Core.Models;
using YamlDotNet.Serialization;

namespace DshPlusPlus.Core.Services;

public sealed class SkillInventoryService
{
    private static readonly Regex SkillNamePattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private SkillImportSettings _settings;

    public SkillInventoryService(SkillImportSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void UpdateSettings(SkillImportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Volatile.Write(ref _settings, settings);
    }

    public Task<IReadOnlyList<SkillInfo>> ScanAsync(CancellationToken cancellationToken)
    {
        var settings = Volatile.Read(ref _settings);
        return Task.Run(() => ScanCore(settings, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<SkillInfo> ScanCore(
        SkillImportSettings settings,
        CancellationToken cancellationToken)
    {
        var result = new List<SkillInfo>();
        ScanRoot(result, settings.CodexSkillsDirectory, SkillSourceKind.Codex,
            settings.DshSkillsDirectory, cancellationToken);
        ScanRoot(result, settings.ClaudeSkillsDirectory, SkillSourceKind.ClaudeCode,
            settings.DshSkillsDirectory, cancellationToken);
        return result
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceKind)
            .ThenBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ScanRoot(
        ICollection<SkillInfo> result,
        string root,
        SkillSourceKind sourceKind,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(root);
            if (SkillContentHasher.IsReparsePoint(normalizedRoot))
                return;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(normalizedRoot)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = Path.GetFileName(entry);
            if (string.Equals(entryName, ".system", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (!SkillContentHasher.IsWithin(entry, normalizedRoot) || SkillContentHasher.IsReparsePoint(entry))
                {
                    if (SkillContentHasher.IsReparsePoint(entry))
                        result.Add(Unsupported(entry, targetRoot, sourceKind, "不导入目录链接或文件链接"));
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    var skillFile = Path.Combine(entry, "SKILL.md");
                    if (File.Exists(skillFile))
                        result.Add(ReadSkill(entry, skillFile, true, sourceKind, targetRoot, cancellationToken));
                    continue;
                }

                if (File.Exists(entry)
                    && string.Equals(Path.GetExtension(entry), ".md", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFileName(entry), "SKILL.md", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(ReadSkill(entry, entry, false, sourceKind, targetRoot, cancellationToken));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                result.Add(new SkillInfo(
                    entryName,
                    string.Empty,
                    sourceKind,
                    entry,
                    string.Empty,
                    Directory.Exists(entry),
                    string.Empty,
                    null,
                    SkillImportState.Error,
                    ex.Message));
            }
        }
    }

    private static SkillInfo ReadSkill(
        string sourcePath,
        string skillFile,
        bool isDirectoryBundle,
        SkillSourceKind sourceKind,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        var fallbackName = isDirectoryBundle
            ? new DirectoryInfo(sourcePath).Name
            : Path.GetFileNameWithoutExtension(sourcePath);
        var parsed = ParseFrontmatter(skillFile);
        if (!parsed.IsValid)
        {
            return new SkillInfo(
                fallbackName,
                parsed.Description,
                sourceKind,
                sourcePath,
                string.Empty,
                isDirectoryBundle,
                string.Empty,
                null,
                SkillImportState.Invalid,
                parsed.Warning);
        }

        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            return new SkillInfo(
                parsed.Name,
                parsed.Description,
                sourceKind,
                sourcePath,
                string.Empty,
                isDirectoryBundle,
                string.Empty,
                null,
                SkillImportState.Unsupported,
                "DSH 技能目标目录为空");
        }

        var targetPath = Path.Combine(
            targetRoot,
            isDirectoryBundle ? parsed.Name : parsed.Name + ".md");
        if (!SkillContentHasher.IsWithin(targetPath, targetRoot))
        {
            return new SkillInfo(
                parsed.Name,
                parsed.Description,
                sourceKind,
                sourcePath,
                string.Empty,
                isDirectoryBundle,
                string.Empty,
                null,
                SkillImportState.Invalid,
                "目标路径超出 DSH 技能目录");
        }

        var sourceHash = SkillContentHasher.Compute(sourcePath, isDirectoryBundle, cancellationToken);
        string? targetHash = null;
        var state = SkillImportState.New;
        string? warning = null;
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            var targetMatchesType = isDirectoryBundle
                ? Directory.Exists(targetPath)
                : File.Exists(targetPath);
            if (!targetMatchesType || SkillContentHasher.IsReparsePoint(targetPath))
            {
                state = SkillImportState.Conflict;
                warning = "目标已存在但类型不一致或是链接";
            }
            else
            {
                targetHash = SkillContentHasher.Compute(targetPath, isDirectoryBundle, cancellationToken);
                state = string.Equals(sourceHash, targetHash, StringComparison.Ordinal)
                    ? SkillImportState.SameContent
                    : SkillImportState.Conflict;
            }
        }

        return new SkillInfo(
            parsed.Name,
            parsed.Description,
            sourceKind,
            sourcePath,
            targetPath,
            isDirectoryBundle,
            sourceHash,
            targetHash,
            state,
            warning);
    }

    private static SkillInfo Unsupported(
        string path,
        string targetRoot,
        SkillSourceKind sourceKind,
        string warning) =>
        new(
            Path.GetFileName(path),
            string.Empty,
            sourceKind,
            path,
            string.IsNullOrWhiteSpace(targetRoot) ? string.Empty : Path.Combine(targetRoot, Path.GetFileName(path)),
            Directory.Exists(path),
            string.Empty,
            null,
            SkillImportState.Unsupported,
            warning);

    private static ParsedFrontmatter ParseFrontmatter(string filePath)
    {
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        if (lines.Length < 3 || !string.Equals(lines[0].TrimStart('\uFEFF').Trim(), "---", StringComparison.Ordinal))
            return ParsedFrontmatter.Invalid("技能文件缺少 frontmatter");

        var end = Array.FindIndex(lines, 1, line =>
            string.Equals(line.Trim(), "---", StringComparison.Ordinal)
            || string.Equals(line.Trim(), "...", StringComparison.Ordinal));
        if (end < 0)
            return ParsedFrontmatter.Invalid("技能 frontmatter 未闭合");

        var yaml = string.Join(Environment.NewLine, lines[1..end]);
        try
        {
            var values = new DeserializerBuilder()
                .WithDuplicateKeyChecking()
                .Build()
                .Deserialize<Dictionary<string, object?>>(yaml);
            var name = ReadValue(values, "name");
            var description = ReadValue(values, "description");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
                return ParsedFrontmatter.Invalid("技能必须包含 name 和 description");
            if (!SkillNamePattern.IsMatch(name))
                return ParsedFrontmatter.Invalid("技能 name 必须使用 kebab-case");
            return new ParsedFrontmatter(true, name, description, null);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return ParsedFrontmatter.Invalid($"技能 frontmatter 无法解析：{ex.Message}");
        }
    }

    private static string? ReadValue(Dictionary<string, object?>? values, string key)
    {
        if (values is null)
            return null;
        var pair = values.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        return pair.Value?.ToString()?.Trim();
    }

    private sealed record ParsedFrontmatter(
        bool IsValid,
        string Name,
        string Description,
        string? Warning)
    {
        public static ParsedFrontmatter Invalid(string warning) =>
            new(false, string.Empty, string.Empty, warning);
    }
}
