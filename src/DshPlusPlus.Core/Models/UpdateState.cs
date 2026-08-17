namespace DshPlusPlus.Core.Models;

public enum UpdateState
{
    NotChecked,
    Checking,
    Latest,
    UpdateAvailable,
    LocalAhead,
    DirtyWorktree,
    CannotConnect,
    InvalidRemote,
    NoUpstream,
    Error
}
