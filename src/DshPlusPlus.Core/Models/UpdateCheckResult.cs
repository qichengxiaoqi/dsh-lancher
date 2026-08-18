namespace DshPlusPlus.Core.Models;

public sealed record UpdateCheckResult(
    UpdateState State,
    string Message,
    RepositorySnapshot? Snapshot = null)
{
    public bool HasUpdate => State is UpdateState.UpdateAvailable or UpdateState.PatchRebaseAvailable;

    // Kept as a compatibility guard for older integrations. DSH updates are notice-only;
    // no application path is allowed to authorize a pull or rebase operation.
    public bool CanPull => false;
}
