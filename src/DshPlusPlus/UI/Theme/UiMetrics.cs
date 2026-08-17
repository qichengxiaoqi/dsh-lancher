namespace DshPlusPlus.UI.Theme;

public readonly record struct NavigationLayoutMode(bool IsCollapsed)
{
    public bool ShowBrandDetails => !IsCollapsed;
    public bool ShowNavigationLabels => !IsCollapsed;
    public bool ShowFooterDetails => !IsCollapsed;
}

public static class UiMetrics
{
    public const int BaseDpi = 96;
    public const int ApiKeyInputMinimumDip = 220;

    public static int ClampFontScale(int value) => Math.Clamp(value, 80, 140);

    public static int PixelsFromDip(int dip, int dpi, int fontScale = 100)
    {
        var safeDip = Math.Max(1, dip);
        var safeDpi = Math.Max(1, dpi);
        var safeFontScale = ClampFontScale(fontScale);
        var pixels = safeDip * safeDpi / (double)BaseDpi * safeFontScale / 100d;
        return Math.Max(1, (int)Math.Round(pixels, MidpointRounding.AwayFromZero));
    }

    public static bool ShouldCollapseNavigation(int clientWidth, int threshold = 1040) => false;

    public static NavigationLayoutMode ResolveNavigationMode(
        bool userCollapsed,
        bool autoCollapse,
        int clientWidth,
        int threshold = 1040) =>
        new(false);

    public static int NavigationWidth(bool collapsed, int expanded = 224, int compact = 78)
    {
        var safeCompact = Math.Max(1, compact);
        return collapsed ? safeCompact : Math.Max(224, expanded);
    }

    public static int SafeHeight(float fontHeight, int verticalPadding, int minimumDip, int dpi)
    {
        var contentHeight = (int)Math.Ceiling(Math.Max(0, fontHeight) + Math.Max(0, verticalPadding));
        var minimumHeight = PixelsFromDip(minimumDip, dpi);
        return Math.Max(1, Math.Max(contentHeight, minimumHeight));
    }
}
