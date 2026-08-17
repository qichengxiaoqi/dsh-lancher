using System.Text.RegularExpressions;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class DshCredentialStore
{
    private static readonly Regex CredentialLine = new(
        "^(?<indent>\\s*)(?<key>[A-Za-z0-9_.-]+)\\s*:\\s*(?<value>.*?)(?<newline>\\r?\\n|$)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CredentialStatus ReadStatus(string filePath, string key)
    {
        var environmentValue = Environment.GetEnvironmentVariable(key);
        var fileValue = File.Exists(filePath) ? ReadValue(File.ReadAllLines(filePath), key) : null;
        var effective = !string.IsNullOrWhiteSpace(environmentValue) ? environmentValue : fileValue;
        return new CredentialStatus(
            HasFileValue: !string.IsNullOrWhiteSpace(fileValue),
            HasEnvironmentOverride: !string.IsNullOrWhiteSpace(environmentValue),
            MaskedValue: Mask(effective),
            CanWrite: CanWrite(filePath),
            Message: !string.IsNullOrWhiteSpace(environmentValue)
                ? "当前由环境变量覆盖"
                : !string.IsNullOrWhiteSpace(fileValue) ? "已配置" : "未配置");
    }

    public string? ReadSecret(string filePath, string key)
    {
        var environmentValue = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue;
        return File.Exists(filePath) ? ReadValue(File.ReadAllLines(filePath), key) : null;
    }

    public async Task SetAsync(
        string filePath,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("凭据不能为空。", nameof(value));

        var lines = File.Exists(filePath)
            ? (await File.ReadAllLinesAsync(filePath, cancellationToken)).ToList()
            : [];
        var replacement = $"{key}: '{Escape(value)}'";
        var replaced = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var match = CredentialLine.Match(lines[index] + "\n");
            if (!match.Success || !string.Equals(match.Groups["key"].Value, key, StringComparison.Ordinal))
                continue;
            lines[index] = replacement;
            replaced = true;
            break;
        }

        if (!replaced)
            lines.Add(replacement);
        await AtomicWriteAsync(filePath, string.Join(Environment.NewLine, lines) + Environment.NewLine, cancellationToken);
    }

    public async Task ClearAsync(
        string filePath,
        string key,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return;

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var kept = lines.Where(line =>
        {
            var match = CredentialLine.Match(line + "\n");
            return !match.Success || !string.Equals(match.Groups["key"].Value, key, StringComparison.Ordinal);
        });
        await AtomicWriteAsync(filePath, string.Join(Environment.NewLine, kept) + Environment.NewLine, cancellationToken);
    }

    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "未设置";
        if (value.Length <= 4)
            return "••••";
        return "••••" + value[^4..];
    }

    private static string? ReadValue(IEnumerable<string> lines, string key)
    {
        foreach (var line in lines)
        {
            var match = CredentialLine.Match(line + "\n");
            if (!match.Success || !string.Equals(match.Groups["key"].Value, key, StringComparison.Ordinal))
                continue;
            return Unquote(match.Groups["value"].Value.Trim());
        }
        return null;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"'))
            ? value[1..^1]
            : value;

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static bool CanWrite(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            return directory is not null && (Directory.Exists(directory) || Directory.CreateDirectory(directory).Exists);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task AtomicWriteAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath)
                        ?? throw new InvalidOperationException("凭据文件目录无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
        try
        {
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
