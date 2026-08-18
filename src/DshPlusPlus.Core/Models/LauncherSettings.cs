namespace DshPlusPlus.Core.Models;

public sealed record ThemeSettings
{
    public string Name { get; init; } = "Obsidian";
    public string Accent { get; init; } = "#39D9FF";
    public bool Glow { get; init; } = true;
    public int FontScale { get; init; } = 100;
    public int Density { get; init; } = 2;
    public int NavigationWidth { get; init; } = 224;
    public bool NavigationCollapsed { get; init; }
    public bool AutoCollapseNavigation { get; init; }
}

public sealed record SkillImportSettings
{
    public string CodexSkillsDirectory { get; init; } = string.Empty;
    public string ClaudeSkillsDirectory { get; init; } = string.Empty;
    public string DshSkillsDirectory { get; init; } = string.Empty;
}

public sealed record LauncherSettings
{
    public const int CurrentSchemaVersion = 6;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool AutoDetectPaths { get; init; } = true;
    public LauncherPaths Paths { get; init; } = LauncherPaths.CreateDefault();
    public DshUpdateSettings DshUpdates { get; init; } = new();
    public ThemeSettings Theme { get; init; } = new();
    public SkillImportSettings SkillImport { get; init; } = new();
    public string StartPage { get; init; } = "DSH 管理";
    public int RefreshSeconds { get; init; } = 10;
    public bool ShowLogDrawer { get; init; } = true;
    public bool CloseToTray { get; init; } = true;
    public bool AutoUpdateEnabled { get; init; } = true;
    public int UpdateCheckIntervalHours { get; init; } = 24;
    public DateTimeOffset? LastUpdateCheckUtc { get; init; }

    public static LauncherSettings CreateDefault() => new();
}

public sealed record PathValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
