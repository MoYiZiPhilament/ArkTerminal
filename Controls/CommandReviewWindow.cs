using System.Windows;
using System.IO;
using System.Windows.Controls;
using ArkTerminal.Core.Context;
using ArkTerminal.Core.Profiles;

namespace ArkTerminal.Controls;

public sealed class CommandReviewWindow : Window
{
    public CommandReviewWindow(Window owner, IReadOnlyList<TerminalProfile> profiles, string cwd, Func<TerminalProfile, string, string, Task<string>> execute, Action copied)
    {
        Owner = owner; Title = Localize.T("Text0295"); Width = 790; Height = 590; MinWidth = 500; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Theme.WindowBackground");
        var panel = new DockPanel { Margin = new(18) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = Localize.T("Text0296"), TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = Localize.T("Text0297"), Margin = new(0, 12, 0, 4) });
        var target = new ComboBox { ItemsSource = profiles, DisplayMemberPath = "Name", SelectedIndex = profiles.Count > 0 ? 0 : -1 }; header.Children.Add(target);
        header.Children.Add(new TextBlock { Text = Localize.T("Text0298"), Margin = new(0, 8, 0, 4) });
        var directory = new TextBox { Text = cwd, MaxLength = 4096 }; header.Children.Add(directory);
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) }; header.Children.Add(warning);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new StackPanel();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
        var buttons = new WrapPanel();
        var copy = new Button { Content = Localize.T("Text0299") }; buttons.Children.Add(copy);
        var run = new Button { Content = Localize.T("Text0300"), IsEnabled = profiles.Count > 0 }; buttons.Children.Add(run);
        var close = new Button { Content = Localize.T("Text0301"), IsCancel = true }; close.Click += (_, _) => Close(); buttons.Children.Add(close);
        footer.Children.Add(buttons); footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var command = new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, MaxLength = ContextLimits.MaxCommandCharacters, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        command.TextChanged += (_, _) => warning.Text = ContextDocument.RiskReminder(command.Text);
        copy.Click += (_, _) =>
        {
            try { ContextText.ValidatePreview(command.Text); Clipboard.SetText(command.Text); copied(); status.Text = Localize.T("Text0302"); }
            catch (Exception error) when (error is ArgumentException or System.Runtime.InteropServices.ExternalException) { status.Text = error.Message; }
        };
        run.Click += async (_, _) =>
        {
            if (target.SelectedItem is not TerminalProfile profile) return;
            var text = command.Text; var path = directory.Text; var snapshot = profile.DeepClone();
            IsEnabled = false;
            try { status.Text = await execute(snapshot, path, text); }
            catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException) { status.Text = Localize.T("Text0303") + error.Message; }
            finally { IsEnabled = true; }
        };
        if (profiles.Count == 0) status.Text = Localize.T("Text0304");
        panel.Children.Add(command); Content = panel;
        Closed += (_, _) => command.Clear();
    }
}
