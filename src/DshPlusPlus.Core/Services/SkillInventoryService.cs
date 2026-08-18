using System.Security.Cryptography;
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
            if (IsReparsePoint(normalizedRoot))
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
                if (!IsWithin(entry, normalizedRoot) || IsReparsePoint(entry))
                {
                    if (IsReparsePoint(entry))
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
        if (!IsWithin(targetPath, targetRoot))
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

        var sourceHash = ComputeHash(sourcePath, isDirectoryBundle, cancellationToken);
        string? targetHash = null;
        var state = SkillImportState.New;
        string? warning = null;
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            var targetMatchesType = isDirectoryBundle
                ? Directory.Exists(targetPath)
                : File.Exists(targetPath);
            if (!targetMatchesType || IsReparsePoint(targetPath))
            {
                state = SkillImportState.Conflict;
                warning = "目标已存在但类型不一致或是链接";
            }
            else
            {
                targetHash = ComputeHash(targetPath, isDirectoryBundle, cancellationToken);
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

    private static string ComputeHash(
        string path,
        bool isDirectoryBundle,
        CancellationToken cancellationToken)
    {
        var root = isDirectoryBundle
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path))!;
        var files = isDirectoryBundle
            ? EnumerateRegularFiles(root, cancellationToken)
            : [Path.GetFullPath(path)];

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = isDirectoryBundle
                ? Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')
                : Path.GetFileName(file);
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);
            var info = new FileInfo(file);
            hash.AppendData(BitConverter.GetBytes(info.Length));
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, options: FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static IReadOnlyList<string> EnumerateRegularFiles(
        string root,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (IsReparsePoint(directory))
                throw new InvalidDataException("技能包包含目录链接");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(entry))
                    throw new InvalidDataException("技能包包含链接文件");
                if (Directory.Exists(entry))
                    pending.Push(entry);
                else if (File.Exists(entry))
                    files.Add(Path.GetFullPath(entry));
            }
        }
        return files;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsWithin(string path, string parent)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedPath,
                   normalizedParent.TrimEnd(Path.DirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
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
