using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ArkTerminal.Visuals;

public sealed class AuroraBackgroundControl : Canvas, IDisposable
{
    private const int BlobSize = 96;
    private readonly Image[] _images = [new(), new(), new()];
    private readonly TranslateTransform[] _translations = [new(), new(), new()];
    private readonly Dictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private CancellationTokenSource? _paletteCancellation;
    private TimeSpan _lastTick;
    private double _phase;
    private double _activity = 1;
    private double _themeOpacityFactor = 1;
    private bool _enabled = true;
    private bool _focused = true;
    private bool _windowVisible = true;
    private bool _reducedMotion;
    private bool _performanceMode;
    private bool _disposed;
    private int _paletteVersion;

    public AuroraBackgroundControl()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        for (var index = 0; index < _images.Length; index++)
        {
            var image = _images[index];
            image.Stretch = Stretch.Fill;
            image.RenderTransform = _translations[index];
            image.Opacity = 0.72;
            Children.Add(image);
        }
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += OnTick;
        SizeChanged += (_, _) => LayoutBlobs();
    }

    public bool IsAnimating => _timer.IsEnabled;
    public long FrameCount { get; private set; }

    public async Task SetPaletteAsync(IReadOnlyList<Color> colors)
    {
        _paletteCancellation?.Cancel();
        _paletteCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _paletteCancellation = cancellation;
        var version = ++_paletteVersion;
        try
        {
            var sources = new BitmapSource[3];
            for (var index = 0; index < sources.Length; index++)
            {
                var color = colors[index % colors.Count];
                var key = color.ToString();
                if (!_cache.TryGetValue(key, out var source))
                {
                    source = await Task.Run(() => CreateBlob(color), cancellation.Token);
                    _cache[key] = source;
                    while (_cache.Count > 12)
                    {
                        _cache.Remove(_cache.Keys.First());
                    }
                }
                sources[index] = source;
            }
            if (_disposed || version != _paletteVersion)
            {
                return;
            }
            for (var index = 0; index < _images.Length; index++)
            {
                _images[index].Source = sources[index];
            }
            LayoutBlobs();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void SetTheme(bool dark)
    {
        _themeOpacityFactor = dark ? 1 : 0.12;
        ApplyFrame(0, true);
    }

    public void SetState(bool enabled, bool focused, bool visible, bool reducedMotion, bool performanceMode)
    {
        _enabled = enabled;
        _focused = focused;
        _windowVisible = visible;
        _reducedMotion = reducedMotion;
        _performanceMode = performanceMode;
        Visibility = enabled && !performanceMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateTimer();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Stop();
        _paletteCancellation?.Cancel();
        _paletteCancellation?.Dispose();
        _cache.Clear();
        foreach (var image in _images)
        {
            image.Source = null;
        }
    }

    private void UpdateTimer()
    {
        _timer.Stop();
        if (!_enabled || _performanceMode || !_windowVisible)
        {
            return;
        }
        _timer.Interval = TimeSpan.FromMilliseconds(_focused ? 33 : 100);
        if (!_reducedMotion)
        {
            _lastTick = _clock.Elapsed;
            _timer.Start();
        }
        else
        {
            ApplyFrame(0, true);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var elapsed = Math.Clamp((now - _lastTick).TotalSeconds, 0, 0.2);
        _lastTick = now;
        _phase += elapsed * (_focused ? 0.16 : 0.06);
        ApplyFrame(elapsed, false);
        FrameCount++;
    }

    private void ApplyFrame(double elapsed, bool immediate)
    {
        var target = _focused ? 1d : 0.25d;
        _activity = immediate ? target : _activity + (target - _activity) * Math.Min(1, elapsed * 4);
        Opacity = (0.18 + _activity * 0.34) * _themeOpacityFactor;
        for (var index = 0; index < _translations.Length; index++)
        {
            var phase = _phase + index * 2.1;
            var scale = Math.Max(0.1, ActualWidth / 1000d);
            _translations[index].X = Math.Sin(phase) * 70 * scale;
            _translations[index].Y = Math.Cos(phase * 0.83) * 35 * scale;
        }
    }

    private void LayoutBlobs()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }
        var width = ActualWidth * 0.82;
        var height = ActualHeight * 1.12;
        for (var index = 0; index < _images.Length; index++)
        {
            _images[index].Width = width;
            _images[index].Height = height;
            SetLeft(_images[index], ActualWidth * (index * 0.29 - 0.18));
            SetTop(_images[index], ActualHeight * (index == 1 ? -0.22 : -0.05));
        }
        ApplyFrame(0, true);
    }

    private static BitmapSource CreateBlob(Color color)
    {
        var pixels = new byte[BlobSize * BlobSize * 4];
        var center = (BlobSize - 1) / 2d;
        for (var y = 0; y < BlobSize; y++)
        {
            for (var x = 0; x < BlobSize; x++)
            {
                var dx = (x - center) / center;
                var dy = (y - center) / center;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                var falloff = Math.Clamp(1 - distance, 0, 1);
                falloff = falloff * falloff * (3 - 2 * falloff);
                var offset = (y * BlobSize + x) * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = (byte)(falloff * 235);
            }
        }
        var bitmap = new WriteableBitmap(BlobSize, BlobSize, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, BlobSize, BlobSize), pixels, BlobSize * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }
}
