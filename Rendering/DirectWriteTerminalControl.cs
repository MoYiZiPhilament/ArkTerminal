using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ArkTerminal.Input;
using ArkTerminal.Search;
using ArkTerminal.Selection;
using ArkTerminal.Terminal.Input;
using ArkTerminal.Terminal.Rendering;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal.Rendering;

public sealed class DirectWriteTerminalControl : FrameworkElement
{
    private readonly D3DImage _image = new();
    private readonly Direct3DSharedSurface _surface;
    private readonly DirectWriteTerminalRenderer _renderer;
    private readonly TerminalCursorMotion _cursorMotion = new();
    private readonly TerminalViewLimits _limits = new();
    private WpfTextInputBridge? _imeHost;
    private ConPtySession? _session;
    private TerminalScreenSnapshot? _snapshot;
    private TerminalFontService? _fonts;
    private TerminalFontOptions _fontOptions = new();
    private TerminalVisualStyle _visualStyle = new(
        Color.FromRgb(30, 30, 30),
        Color.FromRgb(220, 220, 220),
        Color.FromRgb(54, 88, 128),
        Color.FromRgb(110, 95, 40),
        Color.FromRgb(220, 220, 220),
        1);
    private double _attachedFontSize = 14;
    private TerminalScreenChangedEventArgs? _pendingChanges;
    private readonly object _snapshotSync = new();
    private TerminalScreenChangedEventArgs? _queuedChanges;
    private bool _snapshotUpdateQueued;
    private bool _renderQueued;
    private int _renderVersion;
    private bool _fullInvalidation = true;
    private bool _active = true;
    private int _scrollbackOffset;
    private int _lastColumns;
    private int _lastRows;
    private string _composition = string.Empty;
    private TerminalSelection? _selection;
    private bool _selecting;
    private IReadOnlyList<TerminalSearchMatch> _searchMatches = [];
    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;
    private int _activeSearchMatch = -1;
    private int _searchBufferLineCount;
    private readonly System.Windows.Threading.DispatcherTimer _renderRetry;
    private string? _lastRenderError;
    private TerminalScreenSnapshot? _preview;
    private long _publishedFrames;
    private long _redrawRequests;
    private bool _cursorAnimationEnabled = true;
    private bool _cursorRenderingSubscribed;
    private (int Min, int Max)? _presentedCursorBand;
    private bool _presentedCursorVisible;
    public bool HasUserZoom { get; private set; }
    public bool IsInputEnabled { get; set; } = true;
    public bool IsComposing => _imeHost?.OwnsKeyInput == true;
    public long PublishedFrames => Interlocked.Read(ref _publishedFrames);
    public long RedrawRequests => Interlocked.Read(ref _redrawRequests);
    public long EstimatedSharedTextureBytes => _surface.EstimatedSharedTextureBytes;
    public bool RenderPending => _renderQueued;
    public TerminalVisualStyle VisualStyle => _visualStyle;
    public string? ResolvedFontFamily => _fonts?.ResolvedFontFamily;
    public bool CursorAnimationActive => _cursorMotion.IsAnimating;

    public DirectWriteTerminalControl()
    {
        Focusable = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        _surface = new Direct3DSharedSurface(_image);
        _renderer = new DirectWriteTerminalRenderer(_limits);
        _renderRetry = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _renderRetry.Tick += (_, _) =>
        {
            _renderRetry.Stop();
            QueueRender();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _renderRetry.Stop();
                _cursorMotion.Reset();
                SyncCursorAnimationClock();
                _surface.ResetResources();
            }
            else RequestFullRender();
        };
        Loaded += (_, _) =>
        {
            if (IsInputEnabled && _preview is null && _imeHost is null)
            {
                _imeHost = new WpfTextInputBridge();
                _imeHost.CommitText += Submit;
                _imeHost.CompositionChanged += SetComposition;
                AddVisualChild(_imeHost);
                AddLogicalChild(_imeHost);
            }
            RequestFullRender();
        };
        SizeChanged += (_, _) =>
        {
            _surface.CancelFrame();
            RequestFullRender();
            QueueResize();
            ViewportChanged?.Invoke();
            InvalidateArrange();
        };

        GotKeyboardFocus += (_, _) => RequestFullRender();
        LostKeyboardFocus += (_, _) =>
        {
            _imeHost?.CancelComposition();
            RequestFullRender();
        };
        _image.IsFrontBufferAvailableChanged += (_, _) => Dispatcher.BeginInvoke(RequestFullRender);
        Unloaded += (_, _) =>
        {
            _renderRetry.Stop();
            _cursorMotion.Reset();
            SyncCursorAnimationClock();
            _imeHost?.CancelComposition();
            _surface.ResetResources();
        };
    }

    public event Action<string>? InputError;
    public event Action<ArkTerminal.Terminal.Integration.TerminalLink>? LinkRequested;
    public event Action? ViewportChanged;
    public event Action? SearchRequested;
    public void RequestSearch() => SearchRequested?.Invoke();

    public double DpiScale => VisualTreeHelper.GetDpi(this).PixelsPerDip;
    public double CellWidth => _fonts?.CellWidth ?? 8;
    public double LineHeight => _fonts?.LineHeight ?? 18;
    public TerminalScreenSnapshot? CurrentSnapshot => _snapshot;
    public string CompositionText => _composition;
    public double CurrentFontSize => _fontOptions.FontSize;
    public bool IsInputFocused => _imeHost?.IsKeyboardFocused == true || IsKeyboardFocused;
    public int CachedLayoutCount => _fonts?.CachedLayoutCount ?? 0;
    public int CachedBrushCount => _renderer.CachedBrushCount;

    public void Attach(ConPtySession session, TerminalFontOptions? fontOptions = null, int scrollbackOffset = 0)
    {
        Detach();
        _fontOptions = fontOptions ?? new TerminalFontOptions();
        _attachedFontSize = _fontOptions.FontSize;
        HasUserZoom = false;
        _session = session;
        _session.SnapshotChanged += OnSnapshotChanged;
        _scrollbackOffset = scrollbackOffset;
        _snapshot = session.GetSnapshot(_scrollbackOffset);
        _scrollbackOffset = _snapshot.ScrollbackOffset;
        EnsureFonts();
        RequestFullRender();
        Dispatcher.BeginInvoke(() =>
        {
            FocusInput();
            QueueResize();
        });
    }

    public void Detach()
    {
        _renderRetry.Stop();
        _renderVersion++;
        _cursorMotion.Reset();
        SyncCursorAnimationClock();
        _presentedCursorBand = null;
        _presentedCursorVisible = false;
        _imeHost?.CancelComposition();
        if (_session is not null)
        {
            _session.SnapshotChanged -= OnSnapshotChanged;
        }
        _session = null;
        lock (_snapshotSync) _queuedChanges = null;
        _preview = null;
        _snapshot = null;
        _pendingChanges = null;
        _composition = string.Empty;
        _selection = null;
        _selecting = false;
        CancelSearch();
        _searchMatches = [];
        _activeSearchMatch = -1;
        _scrollbackOffset = 0;
        _lastColumns = 0;
        _lastRows = 0;
        _surface.ResetResources();
        _renderer.Dispose();
        _fonts?.Dispose();
        _fonts = null;
    }

    public void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }
        _active = active;
        if (!active)
        {
            _renderRetry.Stop();
            _cursorMotion.Reset();
            SyncCursorAnimationClock();
            _surface.CancelFrame();
        }
        if (active)
        {
            RequestFullRender();
        }
    }

    public void ScrollToBottom()
    {
        _scrollbackOffset = 0;
        RefreshSnapshot();
        RequestFullRender();
    }

    public int SearchMatchCount => _searchMatches.Count;
    public int ActiveSearchMatch => _activeSearchMatch;

    public async Task SearchAsync(string query)
    {
        CancelSearch();
        var version = _searchVersion;
        if (string.IsNullOrEmpty(query) || _session is null)
        {
            _searchMatches = [];
            _activeSearchMatch = -1;
            RequestFullRender();
            return;
        }
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var session = _session;
        _searchCancellation = cancellation;
        try
        {
            await Task.Delay(_limits.SearchDebounce, token);
            var result = await Task.Run(() =>
            {
                var buffer = session.CaptureText();
                return (Buffer: buffer, Matches: TerminalSearchService.Find(buffer, query, _limits.MaxSearchResults, token));
            }, token);
            if (version != _searchVersion || result.Buffer.Generation != _snapshot?.Generation)
            {
                return;
            }
            _searchMatches = result.Matches;
            _searchBufferLineCount = result.Buffer.Lines.Count;
            _activeSearchMatch = _searchMatches.Count == 0 ? -1 : 0;
            if (_activeSearchMatch >= 0)
            {
                ScrollToSearchMatch();
            }
            RequestFullRender();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (version == _searchVersion)
            {
                _searchCancellation?.Dispose();
                _searchCancellation = null;
            }
        }
    }

    public void NavigateSearch(int delta)
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }
        _activeSearchMatch = (_activeSearchMatch + delta + _searchMatches.Count) % _searchMatches.Count;
        ScrollToSearchMatch();
        RequestFullRender();
    }

    public void CancelSearch()
    {
        _searchVersion++;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
    }

    private void ScrollToSearchMatch()
    {
        if (_activeSearchMatch < 0 || _activeSearchMatch >= _searchMatches.Count || _snapshot is null)
        {
            return;
        }
        var match = _searchMatches[_activeSearchMatch];
        _scrollbackOffset = _snapshot.IsAlternateScreen
            ? 0
            : Math.Clamp(_searchBufferLineCount - _snapshot.Rows - match.LineIndex, 0, _snapshot.ScrollbackLines);
        RefreshSnapshot();
    }

    public void SetComposition(string text)
    {
        if (text.Length > 0 && _scrollbackOffset > 0)
        {
            ScrollToBottom();
        }
        if (_composition == text)
        {
            return;
        }
        _composition = text;
        RequestFullRender();
        ViewportChanged?.Invoke();
            InvalidateArrange();
    }

    public void NotifyDpiChanged()
    {
        _surface.CancelFrame();
        RequestFullRender();
        QueueResize();
        ViewportChanged?.Invoke();
            InvalidateArrange();
    }

    public void ApplyFontOptions(TerminalFontOptions options)
    {
        if (_fontOptions == options) return;
        _fontOptions = options;
        RecreateFonts();
    }

    public void SetFontBaseline(TerminalFontOptions options, double? zoom = null)
    {
        _attachedFontSize = options.FontSize;
        HasUserZoom = zoom is not null;
        ApplyFontOptions(zoom is { } size ? options with { FontSize = size } : options);
    }

    public void ResetZoom()
    {
        HasUserZoom = false;
        ApplyFontOptions(_fontOptions with { FontSize = _attachedFontSize });
    }

    public void AttachPreview(TerminalFontOptions options)
    {
        if (_preview is not null && _fontOptions == options) return;
        if (_preview is null)
        {
            Detach();
            var screen = new Terminal.Screen.TerminalScreen(80, 5, 0);
            var decoder = new Terminal.Decoding.TerminalStreamDecoder(screen, new());
            var bytes = System.Text.Encoding.UTF8.GetBytes(Localize.T("Text0410"));
            decoder.Write(bytes, bytes.Length);
            decoder.Complete();
            _preview = screen.GetSnapshot(Guid.Empty);
            _snapshot = _preview;
        }
        SetFontBaseline(options);
        RequestFullRender();
    }

    public void ScrollPages(int direction) => ScrollLines(direction * Math.Max(1, (_snapshot?.Rows ?? 2) - 1));
    public void ScrollToTop() => ScrollLines(_snapshot?.ScrollbackLines ?? 0);
    private void ScrollLines(int lines)
    {
        if (_snapshot is null || _snapshot.IsAlternateScreen) return;
        _imeHost?.CancelComposition();
        _scrollbackOffset = Math.Clamp(_scrollbackOffset + lines, 0, _snapshot.ScrollbackLines);
        RefreshSnapshot();
        RequestFullRender();
    }

    public void ClearBuffer(bool scrollback)
    {
        if (_session is null) return;
        _imeHost?.CancelComposition();
        _selection = null;
        CancelSearch();
        _searchMatches = [];
        _activeSearchMatch = -1;
        _scrollbackOffset = 0;
        if (scrollback) { _session.ClearScrollback(); _scrollbackOffset = 0; }
        else _session.ClearViewport();
        RefreshSnapshot();
        RequestFullRender();
    }

    public async Task SelectAllAsync()
    {
        var session = _session;
        if (session is null) return;
        var text = await Task.Run(session.CaptureText);
        if (_session != session || text.Lines.Count == 0 || text.Generation != _snapshot?.Generation) return;
        _selection = new(new(text.Lines[0].LineId, 0), new(text.Lines[^1].LineId, _snapshot.Columns));
        RequestFullRender();
    }

    public void ApplyVisualStyle(TerminalVisualStyle style)
    {
        if (_visualStyle == style)
        {
            return;
        }
        _visualStyle = style;
        _renderer.Dispose();
        RequestFullRender();
    }

    public void SetAnimationsEnabled(bool enabled)
    {
        if (_cursorAnimationEnabled == enabled) return;
        _cursorAnimationEnabled = enabled;
        _cursorMotion.Reset();
        SyncCursorAnimationClock();
        RequestFullRender();
    }

    public void FocusInput()
    {
        if (!IsInputEnabled)
        {
            return;
        }
        if (_imeHost is not null)
        {
            _imeHost.Focus();
        }
        else
        {
            Focus();
        }
    }

    protected override int VisualChildrenCount => _imeHost is null ? 0 : 1;

    protected override Visual GetVisualChild(int index) => _imeHost ?? throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize)
    {
        _imeHost?.Measure(new Size(Math.Max(1, CellWidth), Math.Max(1, LineHeight)));
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_imeHost is not null)
        {
            var cursor = _snapshot;
            var x = cursor?.CursorVisible == true ? cursor.CursorColumn * CellWidth : 0;
            var y = cursor?.CursorVisible == true ? cursor.CursorRow * LineHeight : 0;
            _imeHost.Arrange(new Rect(x, y, Math.Max(1, CellWidth), Math.Max(1, LineHeight)));
            _imeHost.RefreshCandidatePosition();
        }
        return base.ArrangeOverride(finalSize);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawImage(_image, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        NotifyDpiChanged();
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (_session is null || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            return;
        }
        e.Handled = true;
        if (_imeHost?.OwnsKeyInput != true)
        {
            Submit(e.Text);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_session is null)
        {
            return;
        }
        var modifiers = Keyboard.Modifiers;
        var control = modifiers.HasFlag(ModifierKeys.Control);
        var alt = modifiers.HasFlag(ModifierKeys.Alt);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (_imeHost?.OwnsKeyInput == true) return;
        if (_session.State == SessionState.Running && IsInputFocused && e.Key == Key.Space && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            // TextBox handles Space as an editing command without raising TextInput.
            Submit(" ");
            e.Handled = true;
            return;
        }
        if (control && e.Key is >= Key.A and <= Key.Z)
        {
            var character = (char)('A' + e.Key - Key.A);
            Submit(_session.Win32InputModeEnabled && !alt
                ? TerminalInputEncoder.EncodeWin32ControlCharacter(character)
                : TerminalInputEncoder.EncodeControlCharacter(character, alt));
            e.Handled = true;
            return;
        }
        if (alt && e.Key is >= Key.A and <= Key.Z)
        {
            Submit("\u001b" + char.ToLowerInvariant((char)('a' + e.Key - Key.A)));
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Tab && shift)
        {
            Submit("\u001b[Z");
            e.Handled = true;
            return;
        }

        var terminalKey = e.Key switch
        {
            Key.Enter => TerminalKey.Enter,
            Key.Back => TerminalKey.Backspace,
            Key.Tab => TerminalKey.Tab,
            Key.Escape => TerminalKey.Escape,
            Key.Up => TerminalKey.Up,
            Key.Down => TerminalKey.Down,
            Key.Right => TerminalKey.Right,
            Key.Left => TerminalKey.Left,
            Key.Home => TerminalKey.Home,
            Key.End => TerminalKey.End,
            Key.Insert => TerminalKey.Insert,
            Key.Delete => TerminalKey.Delete,
            Key.PageUp => TerminalKey.PageUp,
            Key.PageDown => TerminalKey.PageDown,
            Key.F1 => TerminalKey.F1,
            Key.F2 => TerminalKey.F2,
            Key.F3 => TerminalKey.F3,
            Key.F4 => TerminalKey.F4,
            Key.F5 => TerminalKey.F5,
            Key.F6 => TerminalKey.F6,
            Key.F7 => TerminalKey.F7,
            Key.F8 => TerminalKey.F8,
            Key.F9 => TerminalKey.F9,
            Key.F10 => TerminalKey.F10,
            Key.F11 => TerminalKey.F11,
            Key.F12 => TerminalKey.F12,
            _ => (TerminalKey?)null
        };
        if (terminalKey is not null)
        {
            Submit(TerminalInputEncoder.EncodeKey(terminalKey.Value, control, alt, _session.ApplicationCursorKeys));
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_snapshot is null)
        {
            return;
        }
        FocusInput();
        var position = HitTest(e.GetPosition(this), snapContinuationToLead: true);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            try
            {
                var link = ArkTerminal.Terminal.Integration.TerminalLinks.Find(_snapshot, position.LineId, position.Column, _session?.ShellStatus.WorkingDirectory);
                if (link is not null) { LinkRequested?.Invoke(link); e.Handled = true; return; }
            }
            catch (ArgumentException error) { InputError?.Invoke(error.Message); e.Handled = true; return; }
        }
        _selection = new TerminalSelection(position, position);
        _selecting = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting || _selection is null)
        {
            return;
        }
        var position = HitTest(e.GetPosition(this), snapContinuationToLead: false);
        _selection = _selection with { Focus = position };
        RequestFullRender();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_selecting)
        {
            return;
        }
        _selecting = false;
        ReleaseMouseCapture();
        if (_selection is not null && _selection.Anchor == _selection.Focus)
        {
            _selection = null;
        }
        RequestFullRender();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Zoom(e.Delta > 0 ? 1 : -1);
        }
        else if (_snapshot is not null && !_snapshot.IsAlternateScreen)
        {
            _imeHost?.CancelComposition();
            var lines = e.Delta / 120 * 3;
            if (lines == 0)
            {
                lines = Math.Sign(e.Delta);
            }
            _scrollbackOffset = Math.Clamp(_scrollbackOffset + lines, 0, _snapshot.ScrollbackLines);
            RefreshSnapshot();
            RequestFullRender();
        }
        e.Handled = true;
    }

    private static (int Min, int Max)? CursorBand(Vector2 position, TerminalScreenSnapshot snapshot)
    {
        if (!snapshot.CursorVisible || snapshot.Lines.Count == 0) return null;
        var last = snapshot.Lines.Count - 1;
        var row = Math.Clamp((int)MathF.Floor(position.Y), 0, last);
        return (row, Math.Clamp(row + 1, 0, last));
    }

    private async void RenderFrame()
    {
        if (!_active || !IsLoaded || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0 || (_session is null && _preview is null))
        {
            _renderQueued = false;
            return;
        }
        var version = _renderVersion;
        var retry = false;
        var retryDelay = TimeSpan.FromMilliseconds(250);
        try
        {
            EnsureFonts();
            var snapshot = _snapshot ??= _preview ?? _session!.GetSnapshot(_scrollbackOffset);
            var cursorPosition = _cursorMotion.Update(snapshot, Stopwatch.GetTimestamp(), _cursorAnimationEnabled);
            var cursorBand = CursorBand(cursorPosition, snapshot);
            if (snapshot.CursorVisible != _presentedCursorVisible || (_cursorMotion.TargetChanged && !_cursorMotion.IsAnimating))
            {
                // Visibility flips, snaps, large jumps and view changes repaint everything once.
                _fullInvalidation = true;
            }
            var scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            _surface.Resize((int)Math.Ceiling(ActualWidth * scale), (int)Math.Ceiling(ActualHeight * scale));
            if (_visualStyle.BackgroundOpacity < 0.999)
            {
                _fullInvalidation = true;
            }
            var lastRow = snapshot.Lines.Count - 1;
            var startRow = 0;
            var endRow = lastRow;
            Int32Rect? dirtyRect = null;
            if (!_fullInvalidation)
            {
                var hasRange = false;
                void Include(int min, int max)
                {
                    if (!hasRange) { startRow = min; endRow = max; hasRange = true; }
                    else { startRow = Math.Min(startRow, min); endRow = Math.Max(endRow, max); }
                }
                if (_pendingChanges is not null)
                {
                    Include(Math.Clamp(_pendingChanges.DirtyStartRow, 0, Math.Max(0, lastRow)), Math.Clamp(_pendingChanges.DirtyEndRow, 0, Math.Max(0, lastRow)));
                }
                if (_cursorMotion.IsAnimating)
                {
                    // Repaint the previous and current cursor rows so the moving outline leaves no trail.
                    if (cursorBand is { } band) Include(band.Min, band.Max);
                    if (_presentedCursorBand is { } previous) Include(previous.Min, previous.Max);
                }
                if (hasRange)
                {
                    startRow = Math.Clamp(startRow, 0, Math.Max(0, lastRow));
                    endRow = Math.Clamp(endRow, startRow, Math.Max(startRow, lastRow));
                    var pixelTop = Math.Clamp((int)Math.Floor(startRow * LineHeight * scale), 0, _surface.PixelHeight);
                    var pixelBottom = Math.Clamp((int)Math.Ceiling((endRow + 1) * LineHeight * scale), pixelTop, _surface.PixelHeight);
                    var pixelWidth = Math.Clamp((int)Math.Ceiling(ActualWidth * scale), 1, _surface.PixelWidth);
                    var pixelHeight = pixelBottom - pixelTop;
                    if (pixelHeight > 0) dirtyRect = new Int32Rect(0, pixelTop, pixelWidth, pixelHeight);
                    else _fullInvalidation = true;
                }
            }
            var presented = await _surface.RenderAsync(
                (float)(96 * scale),
                target => _renderer.Draw(target, snapshot, _fonts!, startRow, endRow, _composition, _selection, _searchMatches, _visualStyle, cursorPosition),
                dirtyRect,
                () => version == _renderVersion);
            if (presented)
            {
                Interlocked.Increment(ref _publishedFrames);
                InvalidateVisual();
                _lastRenderError = null;
                _presentedCursorBand = cursorBand;
                _presentedCursorVisible = snapshot.CursorVisible;
            }
            else
            {
                _fullInvalidation = true;
                // New output can supersede a healthy in-flight frame; draw the latest content immediately.
                retry = version == _renderVersion || _surface.LastRenderWasBusy;
                if (_surface.LastRenderWasBusy || _cursorMotion.IsAnimating) retryDelay = TimeSpan.FromMilliseconds(16);
            }
            if (presented && version == _renderVersion)
            {
                _pendingChanges = null;
                _fullInvalidation = false;
            }
        }
        catch (Exception exception)
        {
            _fullInvalidation = true;
            retry = true;
            if (_lastRenderError != exception.Message)
            {
                _lastRenderError = exception.Message;
                InputError?.Invoke(exception.Message);
            }
        }
        finally
        {
            _renderQueued = false;
            SyncCursorAnimationClock();
            if (_active && IsLoaded && IsVisible && (_session is not null || _preview is not null) && (_pendingChanges is not null || _fullInvalidation))
            {
                if (retry)
                {
                    _renderRetry.Interval = retryDelay;
                    _renderRetry.Start();
                }
                else
                {
                    QueueRender();
                }
            }
        }
    }

    private void SyncCursorAnimationClock()
    {
        var shouldRun = _cursorMotion.IsAnimating && _cursorAnimationEnabled && _active && IsLoaded && IsVisible;
        if (shouldRun == _cursorRenderingSubscribed) return;
        if (shouldRun) System.Windows.Media.CompositionTarget.Rendering += OnCursorRendering;
        else System.Windows.Media.CompositionTarget.Rendering -= OnCursorRendering;
        _cursorRenderingSubscribed = shouldRun;
    }

    private void OnCursorRendering(object? sender, EventArgs e)
    {
        if (!_cursorMotion.IsAnimating)
        {
            SyncCursorAnimationClock();
            return;
        }
        if (!_renderQueued && !_renderRetry.IsEnabled) RequestFullRender(DispatcherPriority.Render);
    }

    private void RequestFullRender(DispatcherPriority priority = DispatcherPriority.Background)
    {
        Interlocked.Increment(ref _redrawRequests);
        _renderVersion++;
        _fullInvalidation = true;
        QueueRender(priority);
    }

    private void QueueRender(DispatcherPriority priority = DispatcherPriority.Background)
    {
        if (_renderQueued || _renderRetry.IsEnabled || !_active || !IsLoaded || !IsVisible || (_session is null && _preview is null))
        {
            return;
        }
        _renderQueued = true;
        Dispatcher.BeginInvoke(priority, RenderFrame);
    }

    private void RefreshSnapshot()
    {
        if (_session is not null)
        {
            _snapshot = _session.GetSnapshot(_scrollbackOffset);
        }
        ViewportChanged?.Invoke();
            InvalidateArrange();
    }

    private void OnSnapshotChanged(object? sender, TerminalScreenChangedEventArgs e)
    {
        lock (_snapshotSync)
        {
            if (_session?.Id != e.SessionId) return;
            _queuedChanges = _queuedChanges is null ? e : new TerminalScreenChangedEventArgs
            {
                SessionId = e.SessionId,
                Generation = Math.Max(_queuedChanges.Generation, e.Generation),
                DirtyStartRow = Math.Min(_queuedChanges.DirtyStartRow, e.DirtyStartRow),
                DirtyEndRow = Math.Max(_queuedChanges.DirtyEndRow, e.DirtyEndRow)
            };
            if (_snapshotUpdateQueued) return;
            _snapshotUpdateQueued = true;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyQueuedSnapshotChanges);
    }

    private void ApplyQueuedSnapshotChanges()
    {
        TerminalScreenChangedEventArgs? e;
        lock (_snapshotSync)
        {
            e = _queuedChanges;
            _queuedChanges = null;
            _snapshotUpdateQueued = false;
        }
        if (e is null || _session is null || e.SessionId != _session.Id)
        {
            return;
        }
        _renderVersion++;
        var oldScrollback = _snapshot?.ScrollbackLines ?? 0;
        var wasAlternate = _snapshot?.IsAlternateScreen;
        if (_scrollbackOffset > 0)
        {
            var latest = _session.GetSnapshot();
            _scrollbackOffset = Math.Clamp(_scrollbackOffset + latest.ScrollbackLines - oldScrollback, 0, latest.ScrollbackLines);
        }
        RefreshSnapshot();
        if (wasAlternate != _snapshot!.IsAlternateScreen)
        {
            _selection = null;
            _searchMatches = [];
            _activeSearchMatch = -1;
            CancelSearch();
            _scrollbackOffset = 0;
            RefreshSnapshot();
        }
        if (_pendingChanges is null)
        {
            _pendingChanges = e;
        }
        else
        {
            _pendingChanges = new TerminalScreenChangedEventArgs
            {
                SessionId = e.SessionId,
                Generation = e.Generation,
                DirtyStartRow = Math.Min(_pendingChanges.DirtyStartRow, e.DirtyStartRow),
                DirtyEndRow = Math.Max(_pendingChanges.DirtyEndRow, e.DirtyEndRow)
            };
        }
        if (_snapshot!.IsAlternateScreen || _snapshot.ScrollbackOffset != 0 || _pendingChanges.DirtyStartRow == 0 && _pendingChanges.DirtyEndRow >= _snapshot.Rows - 1)
        {
            _fullInvalidation = true;
        }
        QueueRender();
    }

    private void EnsureFonts()
    {
        _fonts ??= new TerminalFontService(_fontOptions, _limits);
    }

    private void RecreateFonts()
    {
        _cursorMotion.Reset();
        SyncCursorAnimationClock();
        _fonts?.Dispose();
        _fonts = null;
        EnsureFonts();
        RequestFullRender();
        QueueResize();
        ViewportChanged?.Invoke();
            InvalidateArrange();
    }

    public void Zoom(int delta)
    {
        HasUserZoom = true;
        var size = Math.Clamp(_fontOptions.FontSize + delta, 6, 96);
        ApplyFontOptions(_fontOptions with { FontSize = size });
    }

    private void QueueResize()
    {
        if (_session is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }
        EnsureFonts();
        var columns = Math.Clamp((int)(ActualWidth / CellWidth), 4, short.MaxValue);
        var rows = Math.Clamp((int)(ActualHeight / LineHeight), 2, short.MaxValue);
        if (columns == _lastColumns && rows == _lastRows)
        {
            return;
        }
        _lastColumns = columns;
        _lastRows = rows;
        _ = ResizeAsync(columns, rows);
    }

    private async Task ResizeAsync(int columns, int rows)
    {
        try
        {
            if (_session is not null)
            {
                await _session.ResizeAsync(columns, rows, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            InputError?.Invoke(exception.Message);
        }
    }

    public async Task PasteAsync()
    {
        try
        {
            if (_session is null || !Clipboard.ContainsText())
            {
                return;
            }
            var text = Clipboard.GetText();
            if (_session.BracketedPasteEnabled)
            {
                text = "\u001b[200~" + text + "\u001b[201~";
            }
            await _session.WriteInputAsync(text.AsMemory(), CancellationToken.None);
        }
        catch (Exception exception)
        {
            InputError?.Invoke(exception.Message);
        }
    }

    private TerminalPosition HitTest(Point point, bool snapContinuationToLead)
    {
        var snapshot = _snapshot!;
        var row = Math.Clamp((int)(point.Y / LineHeight), 0, Math.Max(0, snapshot.Lines.Count - 1));
        var line = snapshot.Lines[row];
        var column = Math.Clamp((int)Math.Round(point.X / CellWidth), 0, snapshot.Columns);
        foreach (var cluster in line.Clusters)
        {
            if (cluster.ColumnWidth != 2 || column != cluster.StartColumn + 1)
            {
                continue;
            }
            column = snapContinuationToLead || point.X < (cluster.StartColumn + 1) * CellWidth
                ? cluster.StartColumn
                : cluster.StartColumn + cluster.ColumnWidth;
            break;
        }
        return new TerminalPosition(line.LineId, column);
    }

    public async Task CopySelectionAsync()
    {
        var selection = _selection;
        var session = _session;
        if (selection is null || session is null)
        {
            return;
        }
        try
        {
            var text = await Task.Run(() =>
            {
                var buffer = session.CaptureText();
                if (!buffer.Lines.Any(line => line.LineId == selection.Anchor.LineId) || !buffer.Lines.Any(line => line.LineId == selection.Focus.LineId)) return string.Empty;
                return TerminalTextExtraction.Copy(selection, buffer);
            });
            if (text.Length > 0) Clipboard.SetText(text);
        }
        catch (Exception exception)
        {
            InputError?.Invoke(exception.Message);
        }
    }

    public TerminalTextRange? GetContextSelection(Guid sessionId)
    {
        Dispatcher.VerifyAccess();
        if (_session?.Id != sessionId || _selection is null || _selection.Anchor == _selection.Focus) return null;
        var (start, end) = _selection.Ordered;
        return new(start.LineId, start.Column, end.LineId, end.Column);
    }

    private void Submit(string text)
    {
        if (string.IsNullOrEmpty(text) || _session is null)
        {
            return;
        }
        if (_scrollbackOffset > 0)
        {
            ScrollToBottom();
        }
        _ = SubmitAsync(text);
    }

    private async Task SubmitAsync(string text)
    {
        try
        {
            if (_session is not null)
            {
                await _session.WriteInputAsync(text.AsMemory(), CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            InputError?.Invoke(exception.Message);
        }
    }
}
