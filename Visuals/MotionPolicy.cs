using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ArkTerminal.Visuals;

public static class MotionPolicy
{
    private static readonly Dictionary<FrameworkElement, int> Versions = new();
    private static readonly Duration EnterDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };

    public static void Show(FrameworkElement element, bool animated, double offset = 8)
    {
        if (!animated)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            return;
        }
        var version = Versions.TryGetValue(element, out var current) ? current + 1 : 1;
        Versions[element] = version;
        var transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        element.Opacity = 0;
        transform.Y = offset;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, EnterDuration) { EasingFunction = EaseOut });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, EnterDuration) { EasingFunction = EaseOut });
    }

    public static void Drop(FrameworkElement element, double x, double y, bool animated)
    {
        if (!animated)
        {
            element.RenderTransform = Transform.Identity;
            return;
        }
        var transform = new TranslateTransform(x, y);
        element.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, EnterDuration) { EasingFunction = EaseOut });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, EnterDuration) { EasingFunction = EaseOut });
    }
}
