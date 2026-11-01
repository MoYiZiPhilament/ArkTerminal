using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ArkTerminal.Core.Configuration;
using Microsoft.Win32;

namespace ArkTerminal.Visuals;

public sealed class ThemeService : IDisposable
{
    private HwndSource? _source;
    private AppThemeMode _mode = AppThemeMode.System;

    public event Action? SystemThemeChanged;
    public bool IsDark { get; private set; } = true;

    public void Attach(HwndSource source)
    {
        _source = source;
        _source.AddHook(OnWindowMessage);
    }

    public void Apply(AppThemeMode mode, string accentColor)
    {
        _mode = mode;
        IsDark = mode switch
        {
            AppThemeMode.Light => false,
            AppThemeMode.Dark => true,
            _ => !ReadSystemLightTheme()
        };
        var resources = Application.Current.Resources;
        var theme = resources.MergedDictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("/Visuals/Themes/Light.xaml", StringComparison.Ordinal) == true ||
            dictionary.Source?.OriginalString.EndsWith("/Visuals/Themes/Dark.xaml", StringComparison.Ordinal) == true);
        if (theme is not null)
        {
            resources.MergedDictionaries.Remove(theme);
        }
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/ArkTerminal;component/Visuals/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Relative)
        });
        if (ColorConverter.ConvertFromString(accentColor) is Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources["Theme.AccentBrush"] = brush;
        }
    }

    public Color GetColor(string key) => Application.Current.Resources[key] is Color color ? color : Colors.Magenta;

    public void Dispose()
    {
        _source?.RemoveHook(OnWindowMessage);
        _source = null;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x001A && _mode == AppThemeMode.System)
        {
            SystemThemeChanged?.Invoke();
        }
        return IntPtr.Zero;
    }

    private static bool ReadSystemLightTheme()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0) is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }
}
