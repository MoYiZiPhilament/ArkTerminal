using System.Windows;
using System.Windows.Controls;
using ArkTerminal.Core.Configuration;

namespace ArkTerminal;

public partial class MainWindow
{
    private TerminalTabPosition _tabPosition;
    private bool _tabLayoutInitialized;
    private double _leftTabWidth = 240;

    private void InitializeWindowChrome()
    {
        StateChanged += (_, _) => UpdateCaptionButtons();
        UpdateCaptionButtons();
        UpdateWorkbenchChrome();
    }

    private void UpdateCaptionButtons()
    {
        var maximized = WindowState == WindowState.Maximized;
        WindowFrame.CornerRadius = new CornerRadius(maximized || _visuals.UsesNativeWindowCorners ? 0 : 12);
        WindowFrame.BorderThickness = new Thickness(maximized ? 0 : 1);
        MaximizeWindowButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeWindowButton.ToolTip = maximized ? Localize.T("Text0037") : Localize.T("Text0038");
        System.Windows.Automation.AutomationProperties.SetName(MaximizeWindowButton, maximized ? Localize.T("Text0037") : Localize.T("Text0038"));
    }

    private void UpdateWorkbenchChrome()
    {
        if (LeftTabColumn.ActualWidth >= 180) _leftTabWidth = LeftTabColumn.ActualWidth;
        var sidebar = _tabPosition == TerminalTabPosition.Left && !_settingsVisible;
        LeftTabColumn.MinWidth = 0;
        LeftTabColumn.MaxWidth = sidebar ? 520 : double.PositiveInfinity;
        LeftTabColumn.Width = new GridLength(sidebar ? _leftTabWidth : 0);
        LeftTabColumn.MinWidth = sidebar ? 180 : 0;
        LeftTabSplitter.Visibility = sidebar ? Visibility.Visible : Visibility.Collapsed;
        TabHost.Visibility = _settingsVisible ? Visibility.Collapsed : Visibility.Visible;
        NavigationPanel.Visibility = _settingsVisible || _focusMode ? Visibility.Collapsed : Visibility.Visible;
        CaptionTitle.Visibility = _tabPosition == TerminalTabPosition.Top && !_settingsVisible ? Visibility.Collapsed : Visibility.Visible;
        CaptionTitle.Text = _settingsVisible ? Localize.T("Text0039") : Title;
        AppMenuButton.Content = _settingsVisible ? "\uE72B" : "\uE700";
        AppMenuButton.ToolTip = _settingsVisible ? Localize.T("Text0040") : Localize.T("Text0041");
        System.Windows.Automation.AutomationProperties.SetName(AppMenuButton, _settingsVisible ? Localize.T("Text0040") : Localize.T("Text0041"));
        TerminalShortcutHint.Visibility = _focusMode ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AppMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsVisible)
        {
            _settingsVisible = false;
            ShowActiveWorkbench();
            UpdateWorkbenchChrome();
            if (_attachedTab?.Session is not null) TerminalView.FocusInput();
            return;
        }
        AppMenuButton.ContextMenu.PlacementTarget = AppMenuButton;
        AppMenuButton.ContextMenu.IsOpen = true;
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
