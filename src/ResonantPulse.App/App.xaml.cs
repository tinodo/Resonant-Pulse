using Microsoft.UI.Xaml;

namespace ResonantPulse.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Diagnostics.Write("Unhandled process exception", args.ExceptionObject as Exception);
        UnhandledException += (_, args) =>
            Diagnostics.Write($"Unhandled XAML exception; native message: {args.Message}", args.Exception);
        // Subscribe before either Application or Window XAML is loaded. Native
        // resource-resolution details can be absent from Exception.Message.
        DebugSettings.XamlResourceReferenceFailed += (_, args) =>
            Diagnostics.Write($"XAML resource reference failed: {args.Message}");
        DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
        Diagnostics.Write("Application initialization; startup is stopped");
        InitializeComponent();
        Diagnostics.Write("Application XAML resources initialized");
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
