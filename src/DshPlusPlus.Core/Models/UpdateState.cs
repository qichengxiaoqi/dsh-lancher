namespace DshPlusPlus.Core.Models;

public enum UpdateState
{
    NotChecked,
    Checking,
    Latest,
    UpdateAvailable,
    PatchRebaseAvailable,
    LocalAhead,
    DirtyWorktree,
    CannotConnect,
    InvalidRemote,
    NoUpstream,
    Error
}
