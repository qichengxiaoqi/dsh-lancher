using DshPlusPlus.Core.Models;

namespace DshPlusPlus.UI.Theme;

public enum TrayStatusKind
{
    Connected,
    Disconnected,
    Checking,
    Attention
}

public static class TrayStatusMapper
{
    public static TrayStatusKind From(ServiceState state, bool busy) =>
        busy
            ? TrayStatusKind.Checking
            : state switch
            {
                ServiceState.Running => TrayStatusKind.Connected,
                ServiceState.Stopped => TrayStatusKind.Disconnected,
                ServiceState.StartFailed => TrayStatusKind.Attention,
                _ => TrayStatusKind.Checking
            };
}
