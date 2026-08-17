using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public static class UpdateDecision
{
    public static UpdateState Evaluate(int ahead, int behind, bool dirty)
    {
        if (dirty)
            return UpdateState.DirtyWorktree;

        if (behind > 0)
            return UpdateState.UpdateAvailable;

        if (ahead > 0)
            return UpdateState.LocalAhead;

        return UpdateState.Latest;
    }
}
