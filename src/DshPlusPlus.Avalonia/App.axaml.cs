using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DshPlusPlus.Avalonia.Services;
using DshPlusPlus.Avalonia.Views;

namespace DshPlusPlus.Avalonia;

public sealed class App : global::Avalonia.Application
{
    private AvaloniaAppHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _host = new AvaloniaAppHost();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _host.MainWindow
            };
            desktop.Exit += (_, _) => _host.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
