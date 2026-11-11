using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ArkTerminal.Input;

public sealed class WpfTextInputBridge : TextBox
{
    private bool _composing;
    private bool _suppressCommit;
    private TextComposition? _composition;
    private Rect _candidateAnchor = Rect.Empty;
    private double _candidateDpi;
    private HwndSource? _windowSource;

    public WpfTextInputBridge()
    {
        Focusable = true;
        Opacity = 0.01;
        Background = Brushes.Transparent;
        Foreground = Brushes.Transparent;
        CaretBrush = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        MinWidth = 0;
        MinHeight = 0;
        AcceptsReturn = false;
        AcceptsTab = false;
        IsUndoEnabled = false;
        foreach (var command in new[] { ApplicationCommands.Copy, ApplicationCommands.Cut, ApplicationCommands.Paste, ApplicationCommands.SelectAll })
        {
            CommandBindings.Add(new CommandBinding(command, (_, e) => e.Handled = true, (_, e) => e.CanExecute = false));
        }
        AddHandler(TextCompositionManager.PreviewTextInputStartEvent, new TextCompositionEventHandler(OnComposition));
        AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent, new TextCompositionEventHandler(OnComposition));
        PreviewTextInput += OnTextCommit;
        TextChanged += (_, _) =>
        {
            if (_composing)
            {
                CompositionChanged?.Invoke(Text);
            }
        };
        PreviewLostKeyboardFocus += (_, _) => CancelComposition();
        GotKeyboardFocus += (_, _) => _suppressCommit = false;
        Loaded += (_, _) =>
        {
            _windowSource = PresentationSource.FromVisual(this) as HwndSource;
            _windowSource?.AddHook(OnWindowMessage);
        };
        Unloaded += (_, _) =>
        {
            CancelComposition();
            _windowSource?.RemoveHook(OnWindowMessage);
            _windowSource = null;
        };
    }

    public event Action<string>? CommitText;
    public event Action<string>? CompositionChanged;

    public bool OwnsKeyInput => _composing;

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Window deactivation does not necessarily change WPF's logical keyboard focus.
        // Cancel before the window's normal focus handling lets TSF finalize the text.
        // On re-activation any surviving composition is stale, so cancel it too.
        if (message == 0x0008 || message == 0x0006 && ((wParam.ToInt64() & 0xffff) == 0 || _composing))
        {
            CancelComposition();
        }
        return IntPtr.Zero;
    }

    public void RefreshCandidatePosition()
    {
        if (PresentationSource.FromVisual(this) is null)
        {
            return;
        }
        var anchor = new Rect(PointToScreen(new Point()), RenderSize);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (anchor == _candidateAnchor && dpi == _candidateDpi)
        {
            return;
        }
        _candidateAnchor = anchor;
        _candidateDpi = dpi;
        // Moving a fixed-size TextBox does not arrange its TextBoxView again.
        // Its Updated event is what makes WPF notify TSF of the new text geometry.
        InvalidateChildLayout(this);
    }

    private static void InvalidateChildLayout(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is UIElement element)
            {
                element.InvalidateArrange();
            }
            InvalidateChildLayout(child);
        }
    }

    public void CancelComposition()
    {
        if (!_composing)
        {
            return;
        }
        _suppressCommit = true;
        _composing = false;
        var composition = _composition;
        _composition = null;
        if (composition is not null)
        {
            TextCompositionManager.CompleteComposition(composition);
        }
        Text = string.Empty;
        CompositionChanged?.Invoke(string.Empty);
        Dispatcher.BeginInvoke(() => _suppressCommit = false);
    }

    private void OnComposition(object sender, TextCompositionEventArgs e)
    {
        _suppressCommit = false;
        _composing = true;
        _composition = e.TextComposition;
        CompositionChanged?.Invoke(e.TextComposition?.CompositionText ?? string.Empty);
    }

    private void OnTextCommit(object sender, TextCompositionEventArgs e)
    {
        if (_suppressCommit)
        {
            _suppressCommit = false;
            e.Handled = true;
            return;
        }
        if (e.TextComposition is not null)
        {
            var result = e.TextComposition.Text;
            _composing = false;
            _composition = null;
            CompositionChanged?.Invoke(string.Empty);
            e.Handled = true;
            if (result.Length > 0)
            {
                CommitText?.Invoke(result);
            }
            Dispatcher.BeginInvoke(() => Text = string.Empty);
            return;
        }
        if (!_composing && e.Text.Length > 0)
        {
            CommitText?.Invoke(e.Text);
            e.Handled = true;
        }
    }
}
