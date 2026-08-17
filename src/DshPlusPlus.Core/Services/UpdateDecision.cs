using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public static class UpdateDecision
{
    public static UpdateState Evaluate(
        int ahead,
        int behind,
        bool dirty,
        bool isPatchBranch = false)
    {
        if (dirty)
            return UpdateState.DirtyWorktree;

        if (isPatchBranch && ahead > 0 && behind > 0)
            return UpdateState.PatchRebaseAvailable;

        if (behind > 0)
            return UpdateState.UpdateAvailable;

        if (ahead > 0)
            return UpdateState.LocalAhead;

        return UpdateState.Latest;
    }
}
