namespace DshPlusPlus.Core.Models;

public enum SkillSourceKind
{
    Codex,
    ClaudeCode,
    Custom
}

public enum SkillImportState
{
    New,
    SameContent,
    Conflict,
    Invalid,
    Unsupported,
    Error
}

public sealed record SkillInfo(
    string Name,
    string Description,
    SkillSourceKind SourceKind,
    string SourcePath,
    string TargetPath,
    bool IsDirectoryBundle,
    string SourceSha256,
    string? TargetSha256,
    SkillImportState State,
    string? Warning);

public sealed record SkillImportResult(
    bool Succeeded,
    string Message,
    string? BackupPath,
    bool RequiresRefresh);
