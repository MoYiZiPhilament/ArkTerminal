using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ArkTerminal.Visuals;

public static class MotionPolicy
{
    private sealed class MotionState
    {
        public int Version;
        public bool Hiding;
        public Action? Hidden;
    }

    private static readonly Dictionary<FrameworkElement, MotionState> States = new();
    private static readonly Duration EnterDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly Duration ExitDuration = new(TimeSpan.FromMilliseconds(110));
    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };

    public static bool IsEnabled { get; private set; } = true;

    public static void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        if (enabled) return;
        foreach (var pair in States.ToArray())
        {
            var element = pair.Key;
            var state = pair.Value;
            state.Version++;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            if (element.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.BeginAnimation(TranslateTransform.YProperty, null);
            }
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            if (state.Hiding) CompleteHide(element, state);
            else States.Remove(element);
        }
    }

    public static void Show(FrameworkElement element, double offset = 8) => Show(element, IsEnabled, offset);

    public static void Show(FrameworkElement element, bool animated, double offset = 8)
    {
        var state = Begin(element, out var version);
        element.Visibility = Visibility.Visible;
        element.IsHitTestVisible = true;
        if (!animated || !IsEnabled)
        {
            States.Remove(element);
            Reset(element);
            return;
        }
        var transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        element.Opacity = 0;
        transform.Y = offset;
        var opacity = new DoubleAnimation(1, EnterDuration) { EasingFunction = EaseOut };
        opacity.Completed += (_, _) =>
        {
            if (States.TryGetValue(element, out var current) && ReferenceEquals(state, current) && state.Version == version && !state.Hiding)
            {
                States.Remove(element);
            }
        };
        element.BeginAnimation(UIElement.OpacityProperty, opacity);
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, EnterDuration) { EasingFunction = EaseOut });
    }

    public static void Hide(FrameworkElement element, Action? completed = null, double offset = 4) => Hide(element, IsEnabled, completed, offset);

    public static void Hide(FrameworkElement element, bool animated, Action? completed = null, double offset = 4)
    {
        var state = Begin(element, out var version);
        state.Hiding = true;
        state.Hidden = completed;
        element.IsHitTestVisible = false;
        if (!animated || !IsEnabled || element.Visibility != Visibility.Visible)
        {
            CompleteHide(element, state);
            return;
        }
        var transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        var opacity = new DoubleAnimation(0, ExitDuration) { EasingFunction = EaseOut };
        opacity.Completed += (_, _) =>
        {
            if (state.Version == version && state.Hiding) CompleteHide(element, state);
        };
        element.BeginAnimation(UIElement.OpacityProperty, opacity);
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, ExitDuration) { EasingFunction = EaseOut });
    }

    public static void Drop(FrameworkElement element, double x, double y, bool animated)
    {
        var state = Begin(element, out var version);
        if (!animated || !IsEnabled)
        {
            States.Remove(element);
            element.RenderTransform = Transform.Identity;
            return;
        }
        var transform = new TranslateTransform(x, y);
        element.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, EnterDuration) { EasingFunction = EaseOut });
        var vertical = new DoubleAnimation(0, EnterDuration) { EasingFunction = EaseOut };
        vertical.Completed += (_, _) =>
        {
            if (States.TryGetValue(element, out var current) && ReferenceEquals(state, current) && state.Version == version)
            {
                States.Remove(element);
            }
        };
        transform.BeginAnimation(TranslateTransform.YProperty, vertical);
    }

    private static MotionState Begin(FrameworkElement element, out int version)
    {
        if (!States.TryGetValue(element, out var state))
        {
            state = new MotionState();
            States[element] = state;
        }
        state.Version++;
        state.Hiding = false;
        state.Hidden = null;
        version = state.Version;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (element.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
        }
        return state;
    }

    private static void CompleteHide(FrameworkElement element, MotionState state)
    {
        var completed = state.Hidden;
        state.Hiding = false;
        state.Hidden = null;
        States.Remove(element);
        Reset(element);
        element.Visibility = Visibility.Collapsed;
        completed?.Invoke();
    }

    private static void Reset(FrameworkElement element)
    {
        element.Opacity = 1;
        element.RenderTransform = Transform.Identity;
    }
}
