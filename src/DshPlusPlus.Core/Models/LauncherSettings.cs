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
    public bool AutoCollapseNavigation { get; init; } = true;
}

public sealed record LauncherSettings
{
    public int SchemaVersion { get; init; } = 3;
    public bool AutoDetectPaths { get; init; } = true;
    public LauncherPaths Paths { get; init; } = LauncherPaths.CreateDefault();
    public ThemeSettings Theme { get; init; } = new();
    public string StartPage { get; init; } = "DSH 管理";
    public int RefreshSeconds { get; init; } = 10;
    public bool ShowLogDrawer { get; init; } = true;
    public bool AutoUpdateEnabled { get; init; } = true;
    public int UpdateCheckIntervalHours { get; init; } = 24;
    public DateTimeOffset? LastUpdateCheckUtc { get; init; }

    public static LauncherSettings CreateDefault() => new();
}

public sealed record PathValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
