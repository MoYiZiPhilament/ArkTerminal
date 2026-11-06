using System.Windows;
using ArkTerminal.Core.Localization;
using ArkTerminal.Infrastructure;

namespace ArkTerminal;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private AppHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--elevated-host")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var result = await ArkTerminal.Terminal.Sessions.Elevation.ElevatedHost.RunAsync(e.Args);
            Shutdown(result);
            return;
        }
        _singleInstance = new Mutex(true, @"Local\ArkTerminal.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show(Localize.T("Text0001"), "ArkTerminal", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _host = new AppHost();
            await _host.InitializeAsync(e.Args, new WindowsPackagingContext());
            Localize.Configure(_host.FormalSettings?.Regional);
            ArkTerminal.Localization.LocalizationTreeRefresher.Attach();
            var window = new MainWindow(_host);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            var path = exception is AppDataInitializationException dataException ? dataException.ErrorPath : null;
            var location = string.IsNullOrWhiteSpace(path) ? string.Empty : Localize.F("Text0002", Environment.NewLine, path);
            MessageBox.Show(Localize.F("Text0003", exception.Message, location), "ArkTerminal", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex && _singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
        }
        _singleInstance?.Dispose();
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
