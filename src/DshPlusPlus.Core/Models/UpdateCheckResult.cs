namespace DshPlusPlus.Core.Models;

public sealed record UpdateCheckResult(
    UpdateState State,
    string Message,
    RepositorySnapshot? Snapshot = null)
{
    public bool CanPull => State is UpdateState.UpdateAvailable or UpdateState.PatchRebaseAvailable
                           && Snapshot is { IsDirty: false, ResolvedRemoteRef: not null };
}
