namespace DshPlusPlus.Core.Models;

public enum PluginSourceKind
{
    OfficialBundle,
    LocalPlugin,
    ThirdParty,
    UserPlugin,
    RuntimeOnly,
    Unknown
}

public sealed record RuntimePluginEntry(
    string EntryId,
    string ModuleName,
    bool Enabled,
    string? FiberPhase);

public sealed record PluginInfo(
    string Name,
    string Version,
    string Description,
    string ModuleName,
    string SourcePath,
    PluginSourceKind SourceKind,
    string Profile,
    bool? Enabled,
    string? FiberPhase,
    bool RuntimeAvailable,
    string? EntryId,
    string? ConfigId);

public sealed record PluginToggleResult(
    bool Succeeded,
    string Message,
    string? BackupPath,
    bool RequiresRestart);
