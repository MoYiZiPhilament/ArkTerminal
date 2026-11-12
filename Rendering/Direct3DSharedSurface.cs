using System.Windows;
using System.Windows.Interop;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using Vortice.DXGI;
using AlphaMode = Vortice.DCommon.AlphaMode;
using Direct3D11 = Vortice.Direct3D11.D3D11;
using Direct3D9 = Vortice.Direct3D9.D3D9;
using D2D1 = Vortice.Direct2D1.D2D1;
using D3D9Format = Vortice.Direct3D9.Format;
using DXGIFormat = Vortice.DXGI.Format;

namespace ArkTerminal.Rendering;

public sealed class Direct3DSharedSurface : IDisposable
{
    public long EstimatedSharedTextureBytes => _texture11 is null ? 0 : (long)_pixelWidth * _pixelHeight * 4;
    private readonly D3DImage _image;
    private ID3D11Device? _device11;
    private ID2D1Factory? _factory2D;
    private IDirect3D9Ex? _context9;
    private IDirect3DDevice9Ex? _device9;
    private ID3D11Texture2D? _texture11;
    private IDirect3DTexture9? _texture9;
    private IDirect3DSurface9? _surface9;
    private ID2D1RenderTarget? _renderTarget;
    private ID3D11Query? _gpuEvent;
    private int _pixelWidth;
    private int _pixelHeight;
    private float _dpi;
    private bool _disposed;
    private bool _rendering;
    private bool _resetPending;
    private int _resourceVersion;
    private CancellationTokenSource? _frameCancellation;
    private readonly System.Windows.Threading.DispatcherTimer _cleanupTimer;

    public Direct3DSharedSurface(D3DImage image)
    {
        _image = image;
        _cleanupTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _cleanupTimer.Tick += (_, _) => ReleasePendingResources();
        _image.IsFrontBufferAvailableChanged += OnFrontBufferAvailableChanged;
    }

    public int PixelWidth => _pixelWidth;
    public int PixelHeight => _pixelHeight;
    public bool IsAvailable => _image.IsFrontBufferAvailable && !_disposed;
    internal bool LastRenderWasBusy { get; private set; }

    public async Task<bool> RenderAsync(float dpi, Action<ID2D1RenderTarget> draw, Int32Rect? dirtyRect = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastRenderWasBusy = _rendering;
        if (_rendering || !_image.IsFrontBufferAvailable || _pixelWidth <= 0 || _pixelHeight <= 0)
        {
            return false;
        }
        if (!TryLockImage())
        {
            LastRenderWasBusy = true;
            return false;
        }

        _rendering = true;
        var version = _resourceVersion;
        using var cancellation = new CancellationTokenSource();
        _frameCancellation = cancellation;
        try
        {
            if (_resetPending)
            {
                ReleaseResources();
            }
            EnsureResources(dpi);
            var device = _device11!;
            var gpuEvent = _gpuEvent!;
            var target = _renderTarget!;
            target.BeginDraw();
            draw(target);
            target.EndDraw().CheckError();
            device.ImmediateContext.End(gpuEvent);
            device.ImmediateContext.Flush();

            await Task.Run(() =>
            {
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                var spin = new System.Threading.SpinWait();
                while (true)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    device.DeviceRemovedReason.CheckError();
                    if (device.ImmediateContext.IsDataAvailable(gpuEvent))
                    {
                        return;
                    }
                    if (timeout.Elapsed > TimeSpan.FromMilliseconds(250))
                    {
                        throw new TimeoutException(Localize.T("Text0409"));
                    }
                    spin.SpinOnce();
                }
            });

            // Present the completed frame even if newer output has superseded its content;
            // the next frame repaints the pending changes incrementally. Only a resource
            // reset or a lost front buffer invalidates the drawn surface.
            if (version != _resourceVersion || !_image.IsFrontBufferAvailable)
            {
                return false;
            }
            _image.AddDirtyRect(dirtyRect ?? new Int32Rect(0, 0, _pixelWidth, _pixelHeight));
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            _resetPending = true;
            // D3D9Ex TestCooperativeLevel always succeeds. Check the actual devices on failure.
            _device11?.DeviceRemovedReason.CheckError();
            _device9?.CheckDeviceState(IntPtr.Zero);
            throw;
        }
        finally
        {
            _frameCancellation = null;
            _rendering = false;
            try
            {
                if (_resetPending)
                {
                    ReleaseResources();
                }
            }
            finally
            {
                _image.Unlock();
            }
        }
    }

    public void CancelFrame()
    {
        _resourceVersion++;
        _frameCancellation?.Cancel();
    }

    public void Resize(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        if (_pixelWidth == pixelWidth && _pixelHeight == pixelHeight)
        {
            return;
        }

        ResetResources();
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
    }

    public void ResetResources()
    {
        if (_disposed)
        {
            return;
        }
        CancelFrame();
        _resetPending = true;
        ReleasePendingResources();
    }

    private void ReleasePendingResources()
    {
        if (_rendering || !_resetPending)
        {
            return;
        }
        if (!TryLockImage())
        {
            _cleanupTimer.Start();
            return;
        }
        try
        {
            ReleaseResources();
        }
        finally
        {
            _image.Unlock();
        }
    }

    // Called on the UI thread with D3DImage locked, after the GPU waiter has exited.
    private void ReleaseResources()
    {
        _cleanupTimer.Stop();
        ClearSizeDependentResources();
        _device9?.Dispose();
        _device9 = null;
        _context9?.Dispose();
        _context9 = null;
        _factory2D?.Dispose();
        _factory2D = null;
        _gpuEvent?.Dispose();
        _gpuEvent = null;
        _device11?.Dispose();
        _device11 = null;
        _resetPending = false;
    }

    private bool TryLockImage()
    {
        if (_image.TryLock(new Duration(TimeSpan.Zero)))
        {
            return true;
        }
        // WPF increments its lock count even when TryLock times out.
        _image.Unlock();
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        ResetResources();
        _disposed = true;
        _image.IsFrontBufferAvailableChanged -= OnFrontBufferAvailableChanged;
    }

    private void EnsureResources(float dpi)
    {
        if (_device11 is null)
        {
            _device11 = Direct3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        }
        _gpuEvent ??= _device11.CreateQuery(new QueryDescription(Vortice.Direct3D11.QueryType.Event));
        if (_factory2D is null)
        {
            _factory2D = D2D1.D2D1CreateFactory<ID2D1Factory>();
        }
        if (_context9 is null || _device9 is null)
        {
            _context9 = Direct3D9.Direct3DCreate9Ex();
            var present = new Vortice.Direct3D9.PresentParameters
            {
                Windowed = true,
                SwapEffect = Vortice.Direct3D9.SwapEffect.Discard,
                DeviceWindowHandle = NativeMethods.GetDesktopWindow(),
                PresentationInterval = PresentInterval.Default
            };
            _device9 = _context9.CreateDeviceEx(
                0,
                DeviceType.Hardware,
                IntPtr.Zero,
                CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
                present);
        }
        if (_renderTarget is not null && Math.Abs(_dpi - dpi) < 0.01f)
        {
            return;
        }

        ClearSizeDependentResources();
        _dpi = dpi;
        var description = new Texture2DDescription
        {
            Width = (uint)_pixelWidth,
            Height = (uint)_pixelHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGIFormat.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.Shared
        };
        _texture11 = _device11.CreateTexture2D(description);
        using var surface = _texture11.QueryInterface<IDXGISurface>();
        var properties = new RenderTargetProperties(new PixelFormat(DXGIFormat.B8G8R8A8_UNorm, AlphaMode.Premultiplied));
        _renderTarget = _factory2D.CreateDxgiSurfaceRenderTarget(surface, properties);
        _renderTarget.Dpi = new Vortice.Mathematics.Size(dpi, dpi);

        IntPtr sharedHandle;
        using (var resource = _texture11.QueryInterface<IDXGIResource>())
        {
            sharedHandle = resource.SharedHandle;
        }
        _texture9 = _device9.CreateTexture(
            (uint)_pixelWidth,
            (uint)_pixelHeight,
            1,
            Vortice.Direct3D9.Usage.RenderTarget,
            D3D9Format.A8R8G8B8,
            Pool.Default,
            ref sharedHandle);
        _surface9 = _texture9.GetSurfaceLevel(0);
        SetBackBuffer();
    }

    private void SetBackBuffer()
    {
        if (_surface9 is null || !_image.IsFrontBufferAvailable)
        {
            return;
        }
        _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface9.NativePointer, enableSoftwareFallback: true);
    }

    private void ClearSizeDependentResources()
    {
        // While unavailable, keep WPF's surface reference so availability notifications
        // can resume. Our COM references can be released after the frame waiter exits.
        if (_image.IsFrontBufferAvailable)
        {
            _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero, enableSoftwareFallback: true);
        }
        _renderTarget?.Dispose();
        _renderTarget = null;
        _surface9?.Dispose();
        _surface9 = null;
        _texture9?.Dispose();
        _texture9 = null;
        _texture11?.Dispose();
        _texture11 = null;
    }

    private void OnFrontBufferAvailableChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            ResetResources();
        }
        else
        {
            CancelFrame();
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr GetDesktopWindow();
    }
}
