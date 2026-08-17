namespace DshPlusPlus.Core.Models;

public sealed record LauncherReleaseAsset(
    string Name,
    Uri DownloadUri,
    long? Size,
    string? Digest);

public sealed record LauncherReleaseInfo(
    string TagName,
    Version Version,
    string Name,
    Uri HtmlUri,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    IReadOnlyList<LauncherReleaseAsset> Assets)
{
    public LauncherReleaseAsset? ExecutableAsset => Assets.FirstOrDefault(
        asset => string.Equals(asset.Name, "dsh++.exe", StringComparison.OrdinalIgnoreCase));
}

public sealed record LauncherUpdateCheckResult(
    bool Succeeded,
    bool UpdateAvailable,
    Version CurrentVersion,
    Version? LatestVersion,
    string Message,
    LauncherReleaseInfo? Release = null);

public sealed record LauncherUpdateDownloadResult(
    bool Succeeded,
    string Message,
    string? PreparedPath = null,
    string? Sha256 = null,
    bool DigestVerified = false);
