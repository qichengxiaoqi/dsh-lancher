namespace DshPlusPlus.Core.Models;

public enum SystemInstructionKind
{
    MarkdownInstruction,
    StructuredSettings,
    ProfilePatch,
    SkillLink
}

public sealed record SystemInstructionFileInfo(
    string Path,
    string Scope,
    SystemInstructionKind Kind,
    long Size,
    DateTime LastWriteTime,
    string Sha256,
    bool IsDuplicate,
    bool IsActive,
    string Note);

public sealed record SystemInstructionScanOptions
{
    public int MaxDirectories { get; init; } = 256;
    public int MaxMatchedFiles { get; init; } = 256;
    public int MaxHashBytes { get; init; } = 1024 * 1024;
    public TimeSpan TimeBudget { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromSeconds(15);
}

public sealed record SystemInstructionScanResult(
    IReadOnlyList<SystemInstructionFileInfo> Files,
    int DirectoriesVisited,
    int MatchedFiles,
    bool IsTruncated,
    string StopReason,
    long DurationMilliseconds);
