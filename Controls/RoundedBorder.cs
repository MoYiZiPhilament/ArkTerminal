using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArkTerminal.Controls;

// Border paints rounded corners but does not clip a D3DImage or its children to them.
public sealed class RoundedBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateClip();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CornerRadiusProperty) UpdateClip();
    }

    private void UpdateClip() => Clip = new RectangleGeometry(new Rect(RenderSize), CornerRadius.TopLeft, CornerRadius.TopLeft);
}
