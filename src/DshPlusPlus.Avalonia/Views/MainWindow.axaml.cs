using Avalonia.Controls;
using Avalonia.Platform;

namespace DshPlusPlus.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        try
        {
            Icon = new WindowIcon(AssetLoader.Open(
                new Uri("avares://DshPlusPlus.Avalonia/Assets/dsh-whale.png")));
        }
        catch (Exception)
        {
            // The window remains usable if a platform cannot load the optional icon.
        }
    }
}
