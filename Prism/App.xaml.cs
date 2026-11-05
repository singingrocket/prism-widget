using Microsoft.UI.Xaml;

namespace Prism;

public partial class App : Application
{
    private static Mutex? _singleInstance;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(true, @"Local\Prism.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }
}
