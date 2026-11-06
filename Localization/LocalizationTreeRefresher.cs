using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ArkTerminal.Core.Localization;

namespace ArkTerminal.Localization;

public static class LocalizationTreeRefresher
{
    private static bool _attached;

    public static void Attach()
    {
        if (_attached) return;
        _attached = true;
        Localize.Changed += RefreshWindows;
    }

    private static void RefreshWindows()
    {
        var app = Application.Current;
        if (app is null) return;
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(RefreshWindows);
            return;
        }
        foreach (Window window in app.Windows) Refresh(window, new HashSet<DependencyObject>());
    }

    private static void Refresh(DependencyObject item, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(item)) return;
        Update(item, Window.TitleProperty);
        Update(item, TextBlock.TextProperty);
        Update(item, ContentControl.ContentProperty);
        Update(item, HeaderedContentControl.HeaderProperty);
        Update(item, HeaderedItemsControl.HeaderProperty);
        Update(item, FrameworkElement.ToolTipProperty);
        Update(item, FrameworkContentElement.ToolTipProperty);
        Update(item, AutomationProperties.NameProperty);
        foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>()) Refresh(child, visited);
        if (item is Visual or Visual3D)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(item); index++)
                Refresh(VisualTreeHelper.GetChild(item, index), visited);
        }
    }

    private static void Update(DependencyObject item, DependencyProperty property)
    {
        if (BindingOperations.GetBindingExpression(item, property) is not null) return;
        if (item.GetValue(property) is string value)
        {
            var translated = Localize.RefreshText(value);
            if (translated != value) item.SetValue(property, translated);
        }
    }
}
