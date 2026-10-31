using System.Windows;
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
        _singleInstance = new Mutex(true, @"Local\ArkTerminal.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show("ArkTerminal 已在当前用户会话中运行。第二个实例已退出，未写入配置。", "ArkTerminal", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _host = new AppHost();
            await _host.InitializeAsync(e.Args, new WindowsPackagingContext());
            var window = new MainWindow(_host);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            var path = exception is AppDataInitializationException dataException ? dataException.ErrorPath : null;
            var location = string.IsNullOrWhiteSpace(path) ? string.Empty : $"{Environment.NewLine}路径：{path}";
            MessageBox.Show($"ArkTerminal 初始化失败：{exception.Message}{location}", "ArkTerminal", MessageBoxButton.OK, MessageBoxImage.Error);
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
