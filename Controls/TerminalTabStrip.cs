using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ArkTerminal.Sessions;

namespace ArkTerminal.Controls;

public sealed class TerminalTabStrip : ItemsControl
{
    public static readonly DependencyProperty OrientationExProperty = DependencyProperty.Register(
        nameof(OrientationEx), typeof(Orientation), typeof(TerminalTabStrip), new PropertyMetadata(Orientation.Horizontal));
    private TabViewModel? _dragSource;
    private Point _start;
    private bool _dragging;
    private int _insertionIndex;
    private InsertionAdorner? _indicator;

    public event Action<TabViewModel, int>? Dropped;
    public Orientation OrientationEx { get => (Orientation)GetValue(OrientationExProperty); set => SetValue(OrientationExProperty, value); }

    public TerminalTabStrip()
    {
        LostMouseCapture += (_, _) =>
        {
            if (_dragging)
            {
                CancelDrag();
            }
        };
    }

    public void CancelDrag()
    {
        _dragSource = null;
        _dragging = false;
        ReleaseMouseCapture();
        RemoveIndicator();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        CancelDrag();
        _dragSource = FindTab(e.OriginalSource as DependencyObject);
        if (_dragSource?.IsNewPage == false)
        {
            _start = e.GetPosition(this);
        }
        else
        {
            _dragSource = null;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragSource is null)
        {
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelDrag();
            return;
        }
        if (!_dragging && (e.GetPosition(this) - _start).Length >= 5)
        {
            _dragging = true;
            CaptureMouse();
        }
        if (_dragging)
        {
            _insertionIndex = GetInsertionIndex(e.GetPosition(this));
            ShowIndicator(_insertionIndex);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging && _dragSource is not null)
        {
            Dropped?.Invoke(_dragSource, _insertionIndex);
        }
        CancelDrag();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && _dragging)
        {
            CancelDrag();
            e.Handled = true;
        }
    }

    private TabViewModel? FindTab(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: TabViewModel tab })
            {
                return tab;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private int GetInsertionIndex(Point point)
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index] is TabViewModel { IsNewPage: true })
            {
                continue;
            }
            if (ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container)
            {
                continue;
            }
            var bounds = new Rect(container.TransformToVisual(this).Transform(new Point()), container.RenderSize);
            var after = OrientationEx == Orientation.Horizontal ? point.X > bounds.Left + bounds.Width / 2 : point.Y > bounds.Top + bounds.Height / 2;
            if (!after)
            {
                return index;
            }
        }
        return Items.Count - 1;
    }

    private void ShowIndicator(int index)
    {
        RemoveIndicator();
        if (AdornerLayer.GetAdornerLayer(this) is not { } layer)
        {
            return;
        }
        var offset = new Point();
        if (ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
        {
            var bounds = new Rect(container.TransformToVisual(this).Transform(new Point()), container.RenderSize);
            offset = OrientationEx == Orientation.Horizontal ? new Point(bounds.Left, bounds.Top) : new Point(bounds.Left, bounds.Top);
        }
        _indicator = new InsertionAdorner(this, OrientationEx, offset);
        layer.Add(_indicator);
    }

    private void RemoveIndicator()
    {
        if (_indicator is not null)
        {
            AdornerLayer.GetAdornerLayer(this)?.Remove(_indicator);
            _indicator = null;
        }
    }

    private sealed class InsertionAdorner : Adorner
    {
        private readonly Orientation _orientation;
        private readonly Point _offset;
        public InsertionAdorner(UIElement element, Orientation orientation, Point offset) : base(element)
        {
            _orientation = orientation;
            _offset = offset;
            IsHitTestVisible = false;
        }
        protected override void OnRender(DrawingContext drawingContext)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(70, 136, 241)), 2);
            if (_orientation == Orientation.Horizontal)
            {
                drawingContext.DrawLine(pen, new Point(_offset.X, 0), new Point(_offset.X, RenderSize.Height));
            }
            else
            {
                drawingContext.DrawLine(pen, new Point(0, _offset.Y), new Point(RenderSize.Width, _offset.Y));
            }
        }
    }
}
