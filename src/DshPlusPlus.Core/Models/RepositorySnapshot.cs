namespace DshPlusPlus.Core.Models;

public sealed record RepositorySnapshot(
    string Root,
    string Branch,
    string HeadSha,
    string ShortSha,
    string LocalPackageVersion,
    string? RemotePackageVersion,
    string RemoteUrl,
    string? UpstreamRef,
    string? ResolvedRemoteRef,
    int Ahead,
    int Behind,
    bool IsDirty,
    string? Error = null,
    IReadOnlyList<string>? localOnlyChanges = null)
{
    public IReadOnlyList<string> LocalOnlyChanges => localOnlyChanges ?? Array.Empty<string>();

    public string UpstreamRemoteName { get; init; } = "origin";

    public bool IsPatchBranch { get; init; }

    public IReadOnlyList<string> ProtectedLocalChanges { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> TrackedProtectedChanges { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SourceChanges { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> UnknownChanges { get; init; } = Array.Empty<string>();
}
