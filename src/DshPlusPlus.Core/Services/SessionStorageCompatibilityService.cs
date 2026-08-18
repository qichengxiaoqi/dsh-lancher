using System.Text.RegularExpressions;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public enum SessionStorageFormat
{
    Empty,
    Jsonl,
    Zstd,
    Mixed,
    Unknown
}

public sealed record SessionStorageCompatibilityResult(
    SessionStorageFormat Format,
    bool CanStart,
    bool Changed,
    string Message,
    string? BackupPath = null);

/// <summary>
/// Performs a read-only session format preflight.
/// dsh++ deliberately never edits the session profile, moves session files,
/// creates a quarantine directory, or deletes chat data automatically.
/// </summary>
public sealed class SessionStorageCompatibilityService
{
    private const string SessionFileName = "session.jsonl";
    private const string CompressedSessionFileName = "session.jsonl.zstd";
    private const string SessionPluginId = "session-persistence-jsonl";

    private readonly LauncherPaths _paths;

    public SessionStorageCompatibilityService(LauncherPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<SessionStorageCompatibilityResult> PrepareAsync(
        CancellationToken cancellationToken,
        bool allowMixedQuarantine = false)
    {
        // Kept for API compatibility with older UI/controller builds. The flag
        // is intentionally ignored so no caller can opt into file migration.
        _ = allowMixedQuarantine;
        cancellationToken.ThrowIfCancellationRequested();

        var sessionsRoot = Path.Combine(_paths.DshHome, "sessions");
        SessionStorageFormat format;
        try
        {
            format = DetectFormat(sessionsRoot, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SessionStorageCompatibilityResult(
                SessionStorageFormat.Unknown,
                CanStart: false,
                Changed: false,
                $"无法检查 DSH 会话格式，已阻止启动以保护会话数据：{exception.Message}");
        }

        if (format == SessionStorageFormat.Empty)
            return Ready(format, "未发现旧会话文件，保持 DSH 默认会话格式。");

        if (format == SessionStorageFormat.Mixed)
            return MixedFormatBlocked();

        SessionStorageFormat configuredFormat;
        try
        {
            configuredFormat = await ReadConfiguredFormatAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SessionStorageCompatibilityResult(
                format,
                CanStart: false,
                Changed: false,
                $"无法读取 DSH 会话配置，已阻止启动以保护会话数据：{exception.Message}");
        }

        if (format == SessionStorageFormat.Jsonl && configuredFormat == SessionStorageFormat.Jsonl)
            return Ready(format, "已检测到 JSONL 会话，且 DSH profile 已选择 compression: none；不修改任何文件。");

        if (format == SessionStorageFormat.Zstd && configuredFormat != SessionStorageFormat.Jsonl)
            return Ready(format, "已检测到 zstd 会话，保持 DSH 当前会话格式；不修改任何文件。");

        var physical = format == SessionStorageFormat.Jsonl ? "JSONL" : "zstd";
        var configured = configuredFormat == SessionStorageFormat.Jsonl
            ? "compression: none（JSONL）"
            : "默认或 zstd";
        return new SessionStorageCompatibilityResult(
            format,
            CanStart: false,
            Changed: false,
            $"检测到现有会话为 {physical}，但 DSH profile 当前为 {configured}。dsh++ 不会自动修改配置、迁移、移动或删除聊天记录；请停止 DSH 后手动统一会话格式，再重试启动。");
    }

    private static SessionStorageCompatibilityResult MixedFormatBlocked() =>
        new(
            SessionStorageFormat.Mixed,
            CanStart: false,
            Changed: false,
            "检测到 session.jsonl 与 session.jsonl.zstd 混存。dsh++ 不会自动迁移、移动或删除聊天记录；请停止 DSH 后手动备份并统一会话格式，再重试启动。",
            BackupPath: null);

    private async Task<SessionStorageFormat> ReadConfiguredFormatAsync(
        CancellationToken cancellationToken)
    {
        var patchPath = _paths.ProfilePatchFile;
        if (string.IsNullOrWhiteSpace(patchPath) || !File.Exists(patchPath))
            return SessionStorageFormat.Zstd;

        var content = await File.ReadAllTextAsync(patchPath, cancellationToken);
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();
        var start = FindTopLevelEntry(lines);
        if (start < 0)
            return SessionStorageFormat.Zstd;

        var end = FindNextTopLevelEntry(lines, start + 1);
        var entry = string.Join('\n', lines.Skip(start).Take((end < 0 ? lines.Count : end) - start));
        return Regex.IsMatch(entry, @"(?m)^\s+compression:\s*none\s*$")
            ? SessionStorageFormat.Jsonl
            : SessionStorageFormat.Zstd;
    }

    private static SessionStorageCompatibilityResult Ready(
        SessionStorageFormat format,
        string message) =>
        new(format, CanStart: true, Changed: false, message);

    private static SessionStorageFormat DetectFormat(
        string sessionsRoot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sessionsRoot))
            return SessionStorageFormat.Empty;

        var hasJsonl = false;
        var hasZstd = false;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        };

        foreach (var path in Directory.EnumerateFiles(sessionsRoot, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            if (string.Equals(fileName, SessionFileName, StringComparison.OrdinalIgnoreCase))
                hasJsonl = true;
            else if (string.Equals(fileName, CompressedSessionFileName, StringComparison.OrdinalIgnoreCase))
                hasZstd = true;

            if (hasJsonl && hasZstd)
                return SessionStorageFormat.Mixed;
        }

        return hasJsonl
            ? SessionStorageFormat.Jsonl
            : hasZstd
                ? SessionStorageFormat.Zstd
                : SessionStorageFormat.Empty;
    }

    private static int FindTopLevelEntry(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (Regex.IsMatch(lines[index], $@"^-\s+id:\s*{Regex.Escape(SessionPluginId)}\s*$"))
                return index;
        }
        return -1;
    }

    private static int FindNextTopLevelEntry(IReadOnlyList<string> lines, int start)
    {
        for (var index = start; index < lines.Count; index++)
        {
            if (Regex.IsMatch(lines[index], @"^-\s+id:\s*\S+"))
                return index;
        }
        return -1;
    }
}
