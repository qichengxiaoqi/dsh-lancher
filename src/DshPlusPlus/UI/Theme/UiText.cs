namespace DshPlusPlus.UI.Theme;

public static class UiText
{
    public static string Truncate(string? value, int maxCharacters)
    {
        if (string.IsNullOrEmpty(value) || maxCharacters <= 0)
            return string.Empty;
        if (value.Length <= maxCharacters)
            return value;
        if (maxCharacters == 1)
            return "…";
        return value[..(maxCharacters - 1)] + "…";
    }
}
