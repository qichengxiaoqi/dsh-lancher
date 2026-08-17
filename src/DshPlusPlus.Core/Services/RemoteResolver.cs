using System.Text.RegularExpressions;

namespace DshPlusPlus.Core.Services;

public static partial class RemoteResolver
{
    [GeneratedRegex(
        "^(https://github\\.com/[^/\\s]+/[^/\\s]+(?:\\.git)?/?|git@github\\.com:[^/\\s]+/[^/\\s]+(?:\\.git)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitHubRemoteRegex();

    public static string? Resolve(string? upstream, string? main, string? master)
    {
        foreach (var candidate in new[] { upstream, main, master })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }

        return null;
    }

    public static bool IsGitHubUrl(string? remoteUrl) =>
        remoteUrl is not null && GitHubRemoteRegex().IsMatch(remoteUrl.Trim());
}
