using System.Drawing;
using System.Drawing.Text;

namespace DshPlusPlus.UI.Theme;

public static class UiFontResolver
{
    public static string ChooseAvailableFamily(
        IReadOnlyCollection<string> available,
        params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (available.Any(name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return candidates.LastOrDefault() ?? FontFamily.GenericSansSerif.Name;
    }

    public static string ResolveUiFamily() => ChooseAvailableFamily(
        InstalledFamilies(),
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "Segoe UI",
        "Arial",
        FontFamily.GenericSansSerif.Name);

    public static string ResolveMonoFamily() => ChooseAvailableFamily(
        InstalledFamilies(),
        "Cascadia Mono",
        "Consolas",
        "Courier New",
        FontFamily.GenericMonospace.Name);

    private static IReadOnlyCollection<string> InstalledFamilies()
    {
        try
        {
            using var collection = new InstalledFontCollection();
            return collection.Families.Select(family => family.Name).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
