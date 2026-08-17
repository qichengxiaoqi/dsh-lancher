namespace DshPlusPlus.Core.Models;

public enum ServiceState
{
    Unknown,
    Stopped,
    Starting,
    Running,
    StartFailed,
    Stopping,
    Restarting,
    Updating,
    Error
}
