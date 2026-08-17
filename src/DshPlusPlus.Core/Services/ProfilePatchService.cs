using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class ProfilePatchService
{
    public static string? FindPluginConfigYaml(string packageDirectory, string configId)
    {
        var patchPath = Path.Combine(packageDirectory, "cordis.patch.yml");
        if (!File.Exists(patchPath))
            return $"id: {configId}\nname: {configId}\n";

        var lines = File.ReadAllLines(patchPath);
        var start = Array.FindIndex(lines, line => Regex.IsMatch(
            line,
            $"^\\s*-\\s*id:\\s*{Regex.Escape(configId)}\\b"));
        if (start < 0)
            return $"id: {configId}\nname: {configId}\n";

        var startIndent = lines[start].TakeWhile(char.IsWhiteSpace).Count();
        var output = new List<string>();
        var first = lines[start].TrimStart();
        output.Add(first.StartsWith("- ", StringComparison.Ordinal) ? first[2..] : first);
        for (var index = start + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Trim().Length == 0)
                continue;
            var indent = line.TakeWhile(char.IsWhiteSpace).Count();
            var trimmed = line.TrimStart();
            if (indent <= startIndent && trimmed.StartsWith("- ", StringComparison.Ordinal))
                break;
            output.Add(trimmed);
        }
        return string.Join(Environment.NewLine, output) + Environment.NewLine;
    }

    public async Task<PluginToggleResult> SetPluginEnabledAsync(
        string patchPath,
        string configId,
        string fullConfigYaml,
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            var replacement = ParseMapping(fullConfigYaml);
            replacement.Children[new YamlScalarNode("disabled")] = new YamlScalarNode(enabled ? "false" : "true");
            var sequence = await LoadSequenceAsync(patchPath, cancellationToken);
            var replaced = false;
            for (var index = 0; index < sequence.Children.Count; index++)
            {
                if (sequence.Children[index] is not YamlMappingNode mapping
                    || !string.Equals(ReadScalar(mapping, "id"), configId, StringComparison.Ordinal))
                    continue;
                sequence.Children[index] = replacement;
                replaced = true;
                break;
            }
            if (!replaced)
                sequence.Children.Add(replacement);

            var backupPath = File.Exists(patchPath)
                ? patchPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                : null;
            if (backupPath is not null)
                File.Copy(patchPath, backupPath, overwrite: false);

            var content = Serialize(sequence);
            ValidateSequence(content);
            await AtomicWriteAsync(patchPath, content, cancellationToken);
            return new PluginToggleResult(true, enabled ? "插件已启用" : "插件已禁用", backupPath, true);
        }
        catch (Exception ex) when (ex is InvalidDataException or YamlException or IOException or UnauthorizedAccessException)
        {
            return new PluginToggleResult(false, $"插件配置未修改：{ex.Message}", null, false);
        }
    }

    public static string SerializeConfig(IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder();
        foreach (var pair in values)
            builder.Append(pair.Key).Append(": ").Append(pair.Value).AppendLine();
        return builder.ToString();
    }

    private static async Task<YamlSequenceNode> LoadSequenceAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new YamlSequenceNode();
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith('#'))
            return new YamlSequenceNode();
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlSequenceNode sequence)
            throw new InvalidDataException("cordis.patch.yml 顶层必须是 YAML 数组。");
        return sequence;
    }

    private static YamlMappingNode ParseMapping(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            throw new InvalidDataException("插件配置必须是 YAML 对象。");
        return mapping;
    }

    private static string? ReadScalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value)
            ? (value as YamlScalarNode)?.Value
            : null;

    private static string Serialize(YamlSequenceNode sequence)
    {
        var stream = new YamlStream(new YamlDocument(sequence));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static void ValidateSequence(string content)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlSequenceNode)
            throw new InvalidDataException("生成的插件 patch 不是 YAML 数组。");
    }

    private static async Task AtomicWriteAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("插件 patch 目录无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
        try
        {
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
