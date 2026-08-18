using System.Globalization;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed record LauncherText
{
    public required string WindowTitle { get; init; }
    public required string BrandKicker { get; init; }
    public required string FooterText { get; init; }
    public required string DshManagement { get; init; }
    public required string Maintenance { get; init; }
    public required string DeepSeekApi { get; init; }
    public required string SystemSettings { get; init; }
    public required string PluginSettings { get; init; }
    public required string LauncherSettings { get; init; }
    public required string TrayOpen { get; init; }
    public required string TrayRefresh { get; init; }
    public required string TrayExit { get; init; }
    public required string TrayChecking { get; init; }
    public required string TrayUnknown { get; init; }
    public required string TrayBackground { get; init; }
    public required string LanguageLabel { get; init; }
    public required string LanguageSystem { get; init; }
    public required string LanguageSimplifiedChinese { get; init; }
    public required string LanguageEnglish { get; init; }
    public required string CloseBehavior { get; init; }
    public required string CloseToTray { get; init; }
    public required string SkillCardTitle { get; init; }
    public required string SkillDescription { get; init; }
    public required string SkillPathFormat { get; init; }
    public required string SkillScan { get; init; }
    public required string SkillImportSelected { get; init; }
    public required string SkillSelectColumn { get; init; }
    public required string SkillNameColumn { get; init; }
    public required string SkillDescriptionColumn { get; init; }
    public required string SkillSourceColumn { get; init; }
    public required string SkillStateColumn { get; init; }
    public required string SkillTargetColumn { get; init; }
    public required string SkillNoteColumn { get; init; }
    public required string SkillNotScanned { get; init; }
    public required string SkillScanning { get; init; }
    public required string SkillScanCanceled { get; init; }
    public required string SkillSelectAtLeastOne { get; init; }
    public required string SkillConflictPrompt { get; init; }
    public required string SkillConflictTitle { get; init; }
    public required string SkillRestartHint { get; init; }

    public bool IsEnglish => ReferenceEquals(this, LauncherTextCatalog.English);

    public string Pick(string simplifiedChinese, string english) =>
        IsEnglish ? english : simplifiedChinese;

    public string SkillFound(int count) =>
        this == LauncherTextCatalog.English
            ? $"Found {count} skills. Select New or Conflict items to import."
            : $"发现 {count} 个技能。请选择“新增”或“冲突”项导入。";

    public string SkillScanFailed(string message) =>
        this == LauncherTextCatalog.English
            ? $"Skill scan failed: {message}"
            : $"技能扫描失败：{message}";

    public string SkillImportResult(int succeeded, int failed) =>
        this == LauncherTextCatalog.English
            ? $"Imported {succeeded}; failed {failed}. {SkillRestartHint}"
            : $"已导入 {succeeded} 个；失败 {failed} 个。{SkillRestartHint}";

    public string SkillImportFailed(string message) =>
        this == LauncherTextCatalog.English
            ? $"Skill import failed: {message}"
            : $"技能导入失败：{message}";
}

public static class LauncherTextCatalog
{
    public static LauncherText Chinese { get; } = new()
    {
        WindowTitle = "dsh++ · DeepSeek Harness Control Deck",
        BrandKicker = "DEEPSEEK HARNESS",
        FooterText = "本地控制台\n.NET 9 · WIN-X64",
        DshManagement = "DSH 管理",
        Maintenance = "安装维护",
        DeepSeekApi = "DeepSeek API",
        SystemSettings = "系统级设置",
        PluginSettings = "插件设置",
        LauncherSettings = "启动器设置",
        TrayOpen = "打开 dsh++",
        TrayRefresh = "刷新 DSH 状态",
        TrayExit = "退出 dsh++",
        TrayChecking = "正在检测 DSH",
        TrayUnknown = "DSH 探测异常",
        TrayBackground = "后台托管",
        LanguageLabel = "界面语言",
        LanguageSystem = "跟随系统（默认）",
        LanguageSimplifiedChinese = "简体中文",
        LanguageEnglish = "English",
        CloseBehavior = "关闭行为",
        CloseToTray = "关闭窗口时进入后台托盘",
        SkillCardTitle = "来自 Codex / Claude Code 的技能",
        SkillDescription = "从 Codex 或 Claude Code 选择技能；冲突项替换前会自动备份。",
        SkillPathFormat = "Codex：{0}  |  Claude：{1}  |  DSH 目标：{2}",
        SkillScan = "扫描技能",
        SkillImportSelected = "导入选中项",
        SkillSelectColumn = "导入",
        SkillNameColumn = "名称",
        SkillDescriptionColumn = "描述",
        SkillSourceColumn = "来源",
        SkillStateColumn = "状态",
        SkillTargetColumn = "目标",
        SkillNoteColumn = "说明",
        SkillNotScanned = "尚未扫描技能。",
        SkillScanning = "正在扫描 Codex 和 Claude Code 技能…",
        SkillScanCanceled = "技能扫描已取消。",
        SkillSelectAtLeastOne = "请至少选择一个“新增”或“冲突”技能。",
        SkillConflictPrompt = "目标技能已经存在。替换前会创建带时间戳的备份。是否继续？",
        SkillConflictTitle = "确认替换技能",
        SkillRestartHint = "如果新技能未显示，请重启 DSH。"
    };

    public static LauncherText English { get; } = new()
    {
        WindowTitle = "dsh++ · DeepSeek Harness Control Deck",
        BrandKicker = "DEEPSEEK HARNESS",
        FooterText = "LOCAL CONTROL DECK\n.NET 9 · WIN-X64",
        DshManagement = "DSH Management",
        Maintenance = "Maintenance",
        DeepSeekApi = "DeepSeek API",
        SystemSettings = "System Settings",
        PluginSettings = "Plugin Settings",
        LauncherSettings = "Launcher Settings",
        TrayOpen = "Open dsh++",
        TrayRefresh = "Refresh DSH status",
        TrayExit = "Exit dsh++",
        TrayChecking = "Checking DSH",
        TrayUnknown = "DSH probe failed",
        TrayBackground = "Background",
        LanguageLabel = "Interface language",
        LanguageSystem = "Follow system (default)",
        LanguageSimplifiedChinese = "Simplified Chinese",
        LanguageEnglish = "English",
        CloseBehavior = "Close behavior",
        CloseToTray = "Move to the tray when the window closes",
        SkillCardTitle = "Skills from Codex / Claude Code",
        SkillDescription = "Select skills from Codex or Claude Code; conflicts are backed up before replacement.",
        SkillPathFormat = "Codex: {0}  |  Claude: {1}  |  DSH target: {2}",
        SkillScan = "Scan skills",
        SkillImportSelected = "Import selected",
        SkillSelectColumn = "Import",
        SkillNameColumn = "Name",
        SkillDescriptionColumn = "Description",
        SkillSourceColumn = "Source",
        SkillStateColumn = "State",
        SkillTargetColumn = "Target",
        SkillNoteColumn = "Note",
        SkillNotScanned = "Skills not scanned.",
        SkillScanning = "Scanning Codex and Claude Code skills…",
        SkillScanCanceled = "Skill scan canceled.",
        SkillSelectAtLeastOne = "Select at least one New or Conflict skill.",
        SkillConflictPrompt = "Some target skills already exist. A timestamped backup will be created before replacement. Continue?",
        SkillConflictTitle = "Confirm skill replacement",
        SkillRestartHint = "Restart DSH if a new skill is not visible."
    };

    public static LauncherText Get(
        LauncherLanguage configured,
        CultureInfo? culture = null) =>
        LauncherLanguageResolver.Resolve(configured, culture) == LauncherLanguage.SimplifiedChinese
            ? Chinese
            : English;
}
