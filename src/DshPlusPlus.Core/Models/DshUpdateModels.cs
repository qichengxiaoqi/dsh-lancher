namespace DshPlusPlus.Core.Models;

public sealed record DshUpdateSettings
{
    public string UpstreamRemoteName { get; init; } = "upstream";
    public string PatchBranchName { get; init; } = "dsh++-patches";
    public bool PreferPatchRebase { get; init; } = true;
}

public sealed record DshUpdateLayout(
    string Root,
    string PatchDirectory,
    string WorkspaceDirectory,
    string StateFile)
{
    public static DshUpdateLayout CreateDefault(string? root = null)
    {
        var dataRoot = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dsh++")
            : Path.GetFullPath(root);

        return new(
            dataRoot,
            Path.Combine(dataRoot, "patches", "dsh"),
            Path.Combine(dataRoot, "updates", "dsh"),
            Path.Combine(dataRoot, "dsh-update-state.json"));
    }

    public string CompatibilityFile => Path.Combine(Root, "compatibility.json");
}

public enum DshChangeKind
{
    DshSource,
    ProtectedLocalFile,
    UserData,
    Unknown
}

public sealed record DshRepositoryChange(
    string Path,
    string Status,
    DshChangeKind Kind)
{
    public bool BlocksUpdate => Kind is DshChangeKind.DshSource or DshChangeKind.Unknown;
}

public sealed record DshPatchCommit(string Sha, string Subject);

public sealed record DshCompatibilityManifest(
    string TestedUpstreamSha,
    string TestedDshVersion,
    string PatchSet,
    DateTimeOffset? VerifiedAtUtc = null);

public sealed record DshPatchQueueSnapshot(
    string BranchName,
    bool BranchExists,
    bool IsActive,
    int CommitCount,
    string StoragePath,
    string WorkspacePath,
    IReadOnlyList<DshPatchCommit>? Commits = null)
{
    public IReadOnlyList<DshPatchCommit> PatchCommits => Commits ?? Array.Empty<DshPatchCommit>();
}
