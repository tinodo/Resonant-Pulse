using Microsoft.UI.Xaml;

namespace ResonantPulse.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Diagnostics.Write("Unhandled process exception", args.ExceptionObject as Exception);
        UnhandledException += (_, args) => Diagnostics.Write("Unhandled XAML exception", args.Exception);
        Diagnostics.Write("Application initialization; startup is stopped");
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
            Diagnostics.Write("Native window activated");
        }
        catch (Exception exception)
        {
            Diagnostics.Write("Native window activation failed", exception);
            throw;
        }
    }
}
