using System.Globalization;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public static class LauncherLanguageResolver
{
    public static LauncherLanguage Resolve(
        LauncherLanguage configured,
        CultureInfo? culture = null)
    {
        if (configured is LauncherLanguage.SimplifiedChinese or LauncherLanguage.English)
            return configured;

        var effectiveCulture = culture ?? CultureInfo.CurrentUICulture;
        return effectiveCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? LauncherLanguage.SimplifiedChinese
            : LauncherLanguage.English;
    }
}
