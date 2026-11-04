using System.Globalization;
using System.Text;
using ArkTerminal.Terminal.Rendering;

namespace ArkTerminal.Terminal.Screen;

public sealed class TerminalScreen
{
    private readonly object _sync = new();
    private readonly List<TerminalLine> _scrollback = [];
    private TerminalBuffer _main;
    private TerminalBuffer _alternate;
    private TerminalBuffer _active;
    private int _maxScrollbackLines;
    private readonly long _maxScrollbackBytes;
    private long _scrollbackBytes;
    private int _scrollTop;
    private int _scrollBottom;
    private (int Row, int Column, bool WrapPending) _savedCursor;
    private long _generation;
    private long _nextLogicalLineNumber;
    private int _dirtyStart = int.MaxValue;
    private int _dirtyEnd = -1;

    public TerminalScreen(int columns, int rows, int maxScrollbackLines)
        : this(columns, rows, maxScrollbackLines, Sessions.TerminalSessionOptions.DefaultMaxScrollbackBytes)
    {
    }

    public TerminalScreen(int columns, int rows, int maxScrollbackLines, long maxScrollbackBytes)
    {
        if (columns <= 0 || rows <= 0 || maxScrollbackLines < 0 || maxScrollbackBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }
        _main = new TerminalBuffer(columns, rows);
        _alternate = new TerminalBuffer(columns, rows);
        _active = _main;
        _maxScrollbackLines = maxScrollbackLines;
        _maxScrollbackBytes = maxScrollbackBytes;
        _scrollBottom = rows - 1;
    }

    internal bool AutoWrap { get; private set; } = true;
    internal bool ApplicationCursorKeys { get; private set; }
    internal bool Win32InputMode { get; private set; }
    internal bool BracketedPaste { get; private set; }
    internal bool OriginMode { get; private set; }
    internal bool CursorVisible { get; private set; } = true;
    internal bool IsAlternateScreen => ReferenceEquals(_active, _alternate);
    internal string ShellTitle { get; private set; } = string.Empty;

    internal void SetTitle(string title)
    {
        lock (_sync)
        {
            var normalized = new string(title.Where(character => !char.IsControl(character)).ToArray());
            if (normalized.Length > 1024)
            {
                normalized = normalized[..1024];
            }
            if (ShellTitle == normalized)
            {
                return;
            }
            ShellTitle = normalized;
            MarkDirty(_active.CursorRow, _active.CursorRow);
        }
    }

    internal bool HasChanges
    {
        get
        {
            lock (_sync)
            {
                return _dirtyEnd >= 0;
            }
        }
    }

    internal TerminalModesSnapshot GetModes()
    {
        lock (_sync)
        {
            return new TerminalModesSnapshot(ApplicationCursorKeys, BracketedPaste, AutoWrap, CursorVisible, IsAlternateScreen, Win32InputMode);
        }
    }

    internal string GetShellTitle()
    {
        lock (_sync) return ShellTitle;
    }

    internal void WriteTextElement(string text, int width, TerminalStyle style)
        => WriteTextElementCore(text, width, style, extendPrevious: true);

    private void WriteTextElementCore(string text, int width, TerminalStyle style, bool extendPrevious)
    {
        lock (_sync)
        {
            if (width == 0)
            {
                AppendCombiningText(text);
                return;
            }
            if (extendPrevious && TryExtendPreviousCluster(text))
            {
                return;
            }
            width = _active.Lines[0].Cells.Length == 1 ? 1 : Math.Clamp(width, 1, 2);
            if (_active.WrapPending)
            {
                if (AutoWrap)
                {
                    WrapLine();
                }
                else
                {
                    _active.WrapPending = false;
                }
            }
            if (width == 2 && _active.CursorColumn == Columns - 1)
            {
                if (AutoWrap)
                {
                    WrapLine();
                }
                else
                {
                    width = 1;
                }
            }

            var line = _active.Lines[_active.CursorRow];
            var column = _active.CursorColumn;
            StampOutput(line);
            line.Cells[column].Text = text;
            line.Cells[column].Style = style;
            line.Cells[column].IsContinuation = false;
            line.Cells[column].ColumnWidth = width;
            if (width == 2)
            {
                line.Cells[column + 1].Text = string.Empty;
                line.Cells[column + 1].Style = style;
                line.Cells[column + 1].IsContinuation = true;
                line.Cells[column + 1].ColumnWidth = 0;
            }

            if (column + width >= Columns)
            {
                _active.CursorColumn = Columns - 1;
                _active.WrapPending = true;
            }
            else
            {
                _active.CursorColumn += width;
                _active.WrapPending = false;
            }
            MarkDirty(_active.CursorRow, _active.CursorRow);
        }
    }

    internal void UpdateTextElement(string previousText, string text, TerminalStyle style)
    {
        lock (_sync)
        {
            if (TerminalWidthPolicy.GetColumnWidth(previousText) == 0)
            {
                WriteTextElementCore(text[previousText.Length..], 0, style, extendPrevious: false);
                return;
            }
            var row = _active.CursorRow;
            var column = _active.WrapPending ? _active.CursorColumn : _active.CursorColumn - 1;
            if (column < 0 && row > 0 && _active.Lines[row - 1].IsSoftWrapped)
            {
                row--;
                column = Columns - 1;
            }
            if (column >= 0 && _active.Lines[row].Cells[column].IsContinuation) column--;
            if (column < 0 || _active.Lines[row].Cells[column].Text != previousText)
            {
                WriteTextElementCore(text, TerminalWidthPolicy.GetColumnWidth(text), style, extendPrevious: false);
                return;
            }
            var line = _active.Lines[row];
            var previous = line.Cells[column];
            var width = Columns == 1 ? 1 : Math.Clamp(TerminalWidthPolicy.GetColumnWidth(text), 1, 2);
            if (width == 2 && column == Columns - 1 && !AutoWrap) width = 1;
            if (width == previous.ColumnWidth)
            {
                previous.Text = text;
                MarkDirty(row, row);
                return;
            }
            if (previous.ColumnWidth == 2) line.Cells[column + 1].Clear(style);
            previous.Clear(style);
            _active.CursorRow = row;
            _active.CursorColumn = column;
            _active.WrapPending = false;
            WriteTextElementCore(text, width, style, extendPrevious: false);
        }
    }

    internal void CarriageReturn()
    {
        lock (_sync)
        {
            _active.CursorColumn = 0;
            _active.WrapPending = false;
            MarkDirty(_active.CursorRow, _active.CursorRow);
        }
    }

    internal void LineFeed()
    {
        lock (_sync)
        {
            StampOutput(_active.Lines[_active.CursorRow]);
            LineFeedCore();
        }
    }

    internal void ReverseLineFeed()
    {
        lock (_sync)
        {
            _active.WrapPending = false;
            if (_active.CursorRow == _scrollTop)
            {
                ScrollDownCore(1);
            }
            else if (_active.CursorRow > 0)
            {
                _active.CursorRow--;
            }
            MarkAllDirty();
        }
    }

    internal void Backspace()
    {
        lock (_sync)
        {
            _active.CursorColumn = Math.Max(0, _active.CursorColumn - 1);
            while (_active.Lines[_active.CursorRow].Cells[_active.CursorColumn].IsContinuation && _active.CursorColumn > 0)
            {
                _active.CursorColumn--;
            }
            _active.WrapPending = false;
            MarkDirty(_active.CursorRow, _active.CursorRow);
        }
    }

    internal void Tab()
    {
        lock (_sync)
        {
            _active.CursorColumn = Math.Min(Columns - 1, (_active.CursorColumn / 8 + 1) * 8);
            _active.WrapPending = false;
            MarkDirty(_active.CursorRow, _active.CursorRow);
        }
    }

    internal void MoveCursor(int rowDelta, int columnDelta)
    {
        lock (_sync)
        {
            _active.CursorRow = Math.Clamp(_active.CursorRow + rowDelta, 0, Rows - 1);
            _active.CursorColumn = Math.Clamp(_active.CursorColumn + columnDelta, 0, Columns - 1);
            _active.WrapPending = false;
            MarkDirty(_active.CursorRow, _active.CursorRow);
        }
    }

    internal void SetCursor(int column, int row)
    {
        lock (_sync)
        {
            if (OriginMode)
            {
                row += _scrollTop;
                row = Math.Clamp(row, _scrollTop, _scrollBottom);
            }
            _active.CursorRow = Math.Clamp(row, 0, Rows - 1);
            _active.CursorColumn = Math.Clamp(column, 0, Columns - 1);
            _active.WrapPending = false;
            MarkAllDirty();
        }
    }

    internal void SetCursorColumn(int column) => SetCursor(column, _active.CursorRow);
    internal void SetCursorRow(int row) => SetCursor(_active.CursorColumn, row);

    internal void SaveCursor()
    {
        lock (_sync)
        {
            _savedCursor = (_active.CursorRow, _active.CursorColumn, _active.WrapPending);
        }
    }

    internal void RestoreCursor()
    {
        lock (_sync)
        {
            _active.CursorRow = Math.Clamp(_savedCursor.Row, 0, Rows - 1);
            _active.CursorColumn = Math.Clamp(_savedCursor.Column, 0, Columns - 1);
            _active.WrapPending = _savedCursor.WrapPending;
            MarkAllDirty();
        }
    }

    internal void EraseLine(int mode, TerminalStyle style)
    {
        lock (_sync)
        {
            var line = _active.Lines[_active.CursorRow];
            var start = mode == 1 ? 0 : _active.CursorColumn;
            var end = mode == 0 ? Columns - 1 : _active.CursorColumn;
            if (mode == 2)
            {
                start = 0;
                end = Columns - 1;
            }
            for (var column = start; column <= end; column++)
            {
                line.Cells[column].Clear(style);
            }
            _active.WrapPending = false;
            MarkDirty(_active.CursorRow, _active.CursorRow);
        }
    }

    internal void EraseCharacters(int count, TerminalStyle style)
    {
        lock (_sync)
        {
            var line = _active.Lines[_active.CursorRow];
            var end = Math.Min(Columns, _active.CursorColumn + Math.Max(0, count));
            for (var column = _active.CursorColumn; column < end; column++)
            {
                line.Cells[column].Clear(style);
            }
            _active.WrapPending = false;
            MarkDirty(_active.CursorRow, _active.CursorRow);
        }
    }

    internal void EraseDisplay(int mode, TerminalStyle style)
    {
        lock (_sync)
        {
            if (mode is 2 or 3)
            {
                foreach (var line in _active.Lines)
                {
                    line.Clear(style);
                }
                if (mode == 3 && ReferenceEquals(_active, _main))
                {
                    ClearHistory();
                }
            }
            else if (mode == 0)
            {
                EraseLineRange(_active.CursorRow, _active.CursorColumn, Columns - 1, style);
                for (var row = _active.CursorRow + 1; row < Rows; row++)
                {
                    _active.Lines[row].Clear(style);
                }
            }
            else if (mode == 1)
            {
                for (var row = 0; row < _active.CursorRow; row++)
                {
                    _active.Lines[row].Clear(style);
                }
                EraseLineRange(_active.CursorRow, 0, _active.CursorColumn, style);
            }
            _active.WrapPending = false;
            MarkAllDirty();
        }
    }

    internal void InsertLines(int count)
    {
        lock (_sync)
        {
            if (_active.CursorRow < _scrollTop || _active.CursorRow > _scrollBottom)
            {
                return;
            }
            count = Math.Min(count, _scrollBottom - _active.CursorRow + 1);
            for (var index = 0; index < count; index++)
            {
                _active.Lines.RemoveAt(_scrollBottom);
                _active.Lines.Insert(_active.CursorRow, new TerminalLine(Columns));
            }
            _active.WrapPending = false;
            MarkAllDirty();
        }
    }

    internal void DeleteLines(int count)
    {
        lock (_sync)
        {
            if (_active.CursorRow < _scrollTop || _active.CursorRow > _scrollBottom)
            {
                return;
            }
            count = Math.Min(count, _scrollBottom - _active.CursorRow + 1);
            for (var index = 0; index < count; index++)
            {
                _active.Lines.RemoveAt(_active.CursorRow);
                _active.Lines.Insert(_scrollBottom, new TerminalLine(Columns));
            }
            _active.WrapPending = false;
            MarkAllDirty();
        }
    }

    internal void InsertCharacters(int count)
    {
        lock (_sync)
        {
            count = Math.Min(count, Columns - _active.CursorColumn);
            var line = _active.Lines[_active.CursorRow];
            for (var index = 0; index < count; index++)
            {
                var cells = line.Cells.ToList();
                cells.RemoveAt(Columns - 1);
                cells.Insert(_active.CursorColumn, new TerminalCell());
                line.Cells = cells.ToArray();
            }
            NormalizeWideCells(line);
            MarkDirty(_active.CursorRow, _active.CursorRow);
        }
    }

    internal void DeleteCharacters(int count)
    {
        lock (_sync)
        {
            count = Math.Min(count, Columns - _active.CursorColumn);
            var line = _active.Lines[_active.CursorRow];
            for (var index = 0; index < count; index++)
            {
                var cells = line.Cells.ToList();
                cells.RemoveAt(_active.CursorColumn);
                cells.Add(new TerminalCell());
                line.Cells = cells.ToArray();
            }
            NormalizeWideCells(line);
            MarkDirty(_active.CursorRow, _active.CursorRow);
        }
    }

    internal void ScrollUp(int count)
    {
        lock (_sync)
        {
            ScrollUpCore(Math.Max(1, count));
        }
    }

    internal void ScrollDown(int count)
    {
        lock (_sync)
        {
            ScrollDownCore(Math.Max(1, count));
        }
    }

    internal void SetScrollRegion(int top, int bottom)
    {
        lock (_sync)
        {
            top = Math.Clamp(top, 0, Rows - 1);
            bottom = Math.Clamp(bottom, 0, Rows - 1);
            if (top >= bottom)
            {
                _scrollTop = 0;
                _scrollBottom = Rows - 1;
            }
            else
            {
                _scrollTop = top;
                _scrollBottom = bottom;
            }
            SetCursorCore(0, OriginMode ? _scrollTop : 0);
            MarkAllDirty();
        }
    }

    internal void SetMode(int mode, bool enabled)
    {
        lock (_sync)
        {
            switch (mode)
            {
                case 6:
                    OriginMode = enabled;
                    SetCursorCore(0, enabled ? _scrollTop : 0);
                    break;
                case 7:
                    AutoWrap = enabled;
                    _active.WrapPending = false;
                    break;
                case 25:
                    CursorVisible = enabled;
                    break;
                case 2004:
                    BracketedPaste = enabled;
                    break;
                case 9001:
                    Win32InputMode = enabled;
                    break;
            }
            MarkAllDirty();
        }
    }

    internal void SetApplicationCursorKeys(bool enabled)
    {
        lock (_sync)
        {
            ApplicationCursorKeys = enabled;
        }
    }

    internal void SetAlternateScreen(bool enabled, bool clear, bool saveCursor)
    {
        lock (_sync)
        {
            if (enabled == IsAlternateScreen)
            {
                if (enabled && clear)
                {
                    ClearBuffer(_alternate);
                }
                return;
            }
            if (saveCursor)
            {
                if (enabled)
                {
                    _savedCursor = (_main.CursorRow, _main.CursorColumn, _main.WrapPending);
                }
            }
            _active = enabled ? _alternate : _main;
            if (enabled && clear)
            {
                ClearBuffer(_alternate);
            }
            if (!enabled && saveCursor)
            {
                _main.CursorRow = Math.Clamp(_savedCursor.Row, 0, Rows - 1);
                _main.CursorColumn = Math.Clamp(_savedCursor.Column, 0, Columns - 1);
                _main.WrapPending = _savedCursor.WrapPending;
            }
            _scrollTop = 0;
            _scrollBottom = Rows - 1;
            MarkAllDirty();
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            _main = new TerminalBuffer(Columns, Rows);
            _alternate = new TerminalBuffer(Columns, Rows);
            _active = _main;
            ClearHistory();
            _scrollTop = 0;
            _scrollBottom = Rows - 1;
            AutoWrap = true;
            ApplicationCursorKeys = false;
            Win32InputMode = false;
            BracketedPaste = false;
            OriginMode = false;
            CursorVisible = true;
            _savedCursor = default;
            MarkAllDirty();
        }
    }

    public void Resize(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }
        lock (_sync)
        {
            if (columns == Columns && rows == Rows)
            {
                return;
            }
            var wasAlternate = IsAlternateScreen;
            ReflowMain(columns, rows);
            _alternate = ResizeAlternate(_alternate, columns, rows);
            _active = wasAlternate ? _alternate : _main;
            _scrollTop = 0;
            _scrollBottom = rows - 1;
            MarkAllDirty();
        }
    }

    public TerminalScreenSnapshot GetSnapshot(Guid sessionId, int scrollbackOffset = 0)
    {
        lock (_sync)
        {
            var maximumOffset = IsAlternateScreen ? 0 : _scrollback.Count;
            scrollbackOffset = Math.Clamp(scrollbackOffset, 0, maximumOffset);
            var lines = scrollbackOffset == 0
                ? _active.Lines
                : _scrollback.Concat(_main.Lines).Skip(_scrollback.Count - scrollbackOffset).Take(Rows).ToList();
            return new TerminalScreenSnapshot
            {
                SessionId = sessionId,
                Generation = _generation,
                Columns = Columns,
                Rows = Rows,
                CursorColumn = _active.CursorColumn,
                CursorRow = _active.CursorRow,
                CursorVisible = CursorVisible && scrollbackOffset == 0,
                IsAlternateScreen = IsAlternateScreen,
                ShellTitle = ShellTitle,
                ScrollbackLines = maximumOffset,
                ScrollbackOffset = scrollbackOffset,
                Lines = lines.Select(CreateLineSnapshot).ToArray()
            };
        }
    }

    public TerminalBufferStatistics GetStatistics()
    {
        lock (_sync)
            return new(Columns, Rows, _scrollback.Count, _maxScrollbackLines,
                (long)Columns * (_scrollback.Count + _main.Lines.Count + _alternate.Lines.Count))
            { EstimatedScrollbackBytes = _scrollbackBytes, MaxScrollbackBytes = _maxScrollbackBytes };
    }

    public void ClearViewport()
    {
        lock (_sync)
        {
            foreach (var line in _active.Lines)
            {
                line.Clear(TerminalStyle.Default);
                line.LogicalLineNumber = null;
                line.FirstOutputAtUtc = null;
            }
            MarkAllDirty();
        }
    }

    public void ClearScrollback()
    {
        lock (_sync)
        {
            ClearHistory();
            MarkAllDirty();
        }
    }

    private void StampOutput(TerminalLine line)
    {
        if (line.LogicalLineNumber is not null) return;
        line.LogicalLineNumber = ++_nextLogicalLineNumber;
        line.FirstOutputAtUtc = DateTimeOffset.UtcNow;
    }

    public TerminalBufferTextSnapshot CaptureText(Guid sessionId)
    {
        lock (_sync)
        {
            var lines = IsAlternateScreen ? _active.Lines : _scrollback.Concat(_main.Lines);
            return new TerminalBufferTextSnapshot
            {
                SessionId = sessionId,
                Generation = _generation,
                IsAlternateScreen = IsAlternateScreen,
                Lines = lines.Select(CreateLogicalLine).ToArray()
            };
        }
    }

    public TerminalTextSlice CaptureBoundedText(int maximumLines, int maximumCharacters, TerminalTextRange? selection = null)
    {
        maximumLines = Math.Clamp(maximumLines, 1, Core.Context.ContextLimits.MaxLines);
        maximumCharacters = Math.Clamp(maximumCharacters, 1, Core.Context.ContextLimits.MaxBytes);
        lock (_sync)
        {
            var historyCount = IsAlternateScreen ? 0 : _scrollback.Count;
            var total = historyCount + _active.Lines.Count;
            TerminalLine Line(int index) => index < historyCount ? _scrollback[index] : _active.Lines[index - historyCount];
            var start = Math.Max(0, total - maximumLines);
            var end = total - 1;
            var truncated = start > 0;
            if (selection is not null)
            {
                start = -1; end = -1;
                for (var i = 0; i < total; i++)
                {
                    if (Line(i).Id == selection.StartLine) start = i;
                    if (Line(i).Id == selection.EndLine) end = i;
                }
                if (start < 0 || end < start) return new(string.Empty, false, false, IsAlternateScreen);
                truncated = end - start + 1 > maximumLines;
                end = Math.Min(end, start + maximumLines - 1);
            }
            var chunks = new List<string>();
            var remaining = maximumCharacters;
            for (var offset = 0; offset <= end - start; offset++)
            {
                var index = selection is null ? end - offset : start + offset;
                var line = Line(index);
                var first = selection is not null && line.Id == selection.StartLine ? selection.StartColumn : 0;
                var last = selection is not null && line.Id == selection.EndLine ? selection.EndColumn : line.Cells.Length;
                last = Math.Min(last, line.Cells.Length);
                var occupied = Array.FindLastIndex(line.Cells, cell => !cell.IsContinuation && cell.Text.Length > 0);
                if (last >= line.Cells.Length) last = occupied + 1;
                var pieces = new List<string>();
                var newline = !line.IsSoftWrapped && index < end;
                if (newline && remaining > 0) remaining--;
                for (var n = 0; n < last - Math.Max(0, first); n++)
                {
                    var column = selection is null ? last - n - 1 : Math.Max(0, first) + n;
                    var cell = line.Cells[column];
                    if (cell.IsContinuation) continue;
                    var text = cell.Text.Length == 0 ? " " : cell.Text;
                    if (text.Length > remaining) { truncated = true; remaining = 0; break; }
                    pieces.Add(text); remaining -= text.Length;
                }
                if (selection is null) pieces.Reverse();
                chunks.Add(string.Concat(pieces) + (newline ? "\n" : string.Empty));
                if (remaining == 0) { truncated |= offset < end - start; break; }
            }
            if (selection is null) chunks.Reverse();
            return new(string.Concat(chunks).TrimEnd('\r', '\n'), truncated, true, IsAlternateScreen);
        }
    }

    internal TerminalScreenChangedEventArgs? FlushChanges(Guid sessionId)
    {
        lock (_sync)
        {
            if (_dirtyEnd < 0)
            {
                return null;
            }
            var args = new TerminalScreenChangedEventArgs
            {
                SessionId = sessionId,
                Generation = ++_generation,
                DirtyStartRow = _dirtyStart,
                DirtyEndRow = _dirtyEnd
            };
            _dirtyStart = int.MaxValue;
            _dirtyEnd = -1;
            return args;
        }
    }

    private int Columns => _active.Lines[0].Cells.Length;
    private int Rows => _active.Lines.Count;

    private void ClearHistory()
    {
        _scrollback.Clear();
        _scrollbackBytes = 0;
    }

    private TerminalLine? TrimHistory()
    {
        var removed = 0;
        var reusable = _scrollback.Count > 0 ? _scrollback[0] : null;
        while (removed < _scrollback.Count &&
            (_scrollback.Count - removed > _maxScrollbackLines || _scrollbackBytes > _maxScrollbackBytes))
        {
            _scrollbackBytes -= _scrollback[removed++].EstimatedStorageBytes;
        }
        if (removed > 0) _scrollback.RemoveRange(0, removed);
        return removed > 0 ? reusable : null;
    }

    private bool TryExtendPreviousCluster(string text)
    {
        var line = _active.Lines[_active.CursorRow];
        var column = _active.WrapPending ? _active.CursorColumn : _active.CursorColumn - 1;
        if (column < 0)
        {
            return false;
        }
        if (line.Cells[column].IsContinuation)
        {
            column--;
        }
        if (column < 0)
        {
            return false;
        }

        var previous = line.Cells[column];
        var extendJoiner = previous.Text.EndsWith('\u200D');
        var extendRegionalPair = IsSingleRegionalIndicator(previous.Text) && IsSingleRegionalIndicator(text);
        if (!extendJoiner && !extendRegionalPair)
        {
            return false;
        }
        var combined = previous.Text + text;
        if (StringInfo.GetNextTextElementLength(combined) != combined.Length) return false;
        UpdateTextElement(previous.Text, combined, previous.Style);
        return true;
    }

    private static bool IsSingleRegionalIndicator(string text)
    {
        if (text.Length == 0 || !Rune.TryGetRuneAt(text, 0, out var rune) || rune.Utf16SequenceLength != text.Length)
        {
            return false;
        }
        return rune.Value is >= 0x1F1E6 and <= 0x1F1FF;
    }

    private void AppendCombiningText(string text)
    {
        var line = _active.Lines[_active.CursorRow];
        StampOutput(line);
        var column = _active.CursorColumn;
        if (line.Cells[column].IsContinuation && column > 0)
        {
            column--;
        }
        if (line.Cells[column].Text.Length == 0 && column > 0)
        {
            column--;
            if (line.Cells[column].IsContinuation && column > 0)
            {
                column--;
            }
        }
        line.Cells[column].Text += text;
        MarkDirty(_active.CursorRow, _active.CursorRow);
    }

    private void WrapLine()
    {
        var previous = _active.Lines[_active.CursorRow];
        var logicalLineNumber = previous.LogicalLineNumber;
        var firstOutputAtUtc = previous.FirstOutputAtUtc;
        _active.Lines[_active.CursorRow].IsSoftWrapped = true;
        _active.WrapPending = false;
        _active.CursorColumn = 0;
        LineFeedCore();
        _active.Lines[_active.CursorRow].LogicalLineNumber = logicalLineNumber;
        _active.Lines[_active.CursorRow].FirstOutputAtUtc = firstOutputAtUtc;
    }

    private void LineFeedCore()
    {
        _active.WrapPending = false;
        if (_active.CursorRow == _scrollBottom)
        {
            ScrollUpCore(1);
        }
        else if (_active.CursorRow < Rows - 1)
        {
            _active.CursorRow++;
        }
        MarkAllDirty();
    }

    private void ScrollUpCore(int count)
    {
        count = Math.Min(count, _scrollBottom - _scrollTop + 1);
        var columns = Columns;
        for (var index = 0; index < count; index++)
        {
            var removed = _active.Lines[_scrollTop];
            _active.Lines.RemoveAt(_scrollTop);
            TerminalLine? reusable = null;
            if (ReferenceEquals(_active, _main) && _scrollTop == 0 && _maxScrollbackLines > 0)
            {
                _scrollback.Add(removed);
                _scrollbackBytes += removed.EstimatedStorageBytes;
                reusable = TrimHistory();
            }
            if (reusable is not null) reusable.Reuse();
            _active.Lines.Insert(_scrollBottom, reusable ?? new TerminalLine(columns));
        }
        MarkAllDirty();
    }

    private void ScrollDownCore(int count)
    {
        count = Math.Min(count, _scrollBottom - _scrollTop + 1);
        for (var index = 0; index < count; index++)
        {
            _active.Lines.RemoveAt(_scrollBottom);
            _active.Lines.Insert(_scrollTop, new TerminalLine(Columns));
        }
        MarkAllDirty();
    }

    private void EraseLineRange(int row, int start, int end, TerminalStyle style)
    {
        for (var column = start; column <= end; column++)
        {
            _active.Lines[row].Cells[column].Clear(style);
        }
        NormalizeWideCells(_active.Lines[row]);
    }

    private static void NormalizeWideCells(TerminalLine line)
    {
        for (var column = 0; column < line.Cells.Length; column++)
        {
            var cell = line.Cells[column];
            if (cell.IsContinuation &&
                (column == 0 || line.Cells[column - 1].ColumnWidth != 2 || line.Cells[column - 1].IsContinuation))
            {
                cell.Clear(cell.Style);
                continue;
            }
            if (cell.ColumnWidth == 2 &&
                (column + 1 >= line.Cells.Length || !line.Cells[column + 1].IsContinuation))
            {
                cell.Clear(cell.Style);
            }
        }
    }

    private void SetCursorCore(int column, int row)
    {
        _active.CursorColumn = Math.Clamp(column, 0, Columns - 1);
        _active.CursorRow = Math.Clamp(row, 0, Rows - 1);
        _active.WrapPending = false;
    }

    private static void ClearBuffer(TerminalBuffer buffer)
    {
        foreach (var line in buffer.Lines)
        {
            line.Clear(TerminalStyle.Default);
        }
        buffer.CursorRow = 0;
        buffer.CursorColumn = 0;
        buffer.WrapPending = false;
    }

    private TerminalBuffer ResizeAlternate(TerminalBuffer source, int columns, int rows)
    {
        var target = new TerminalBuffer(columns, rows);
        for (var row = 0; row < Math.Min(rows, source.Lines.Count); row++)
        {
            for (var column = 0; column < Math.Min(columns, source.Lines[row].Cells.Length); column++)
            {
                target.Lines[row].Cells[column] = source.Lines[row].Cells[column].Clone();
            }
            target.Lines[row].IsSoftWrapped = source.Lines[row].IsSoftWrapped;
            target.Lines[row].LogicalLineNumber = source.Lines[row].LogicalLineNumber;
            target.Lines[row].FirstOutputAtUtc = source.Lines[row].FirstOutputAtUtc;
        }
        target.CursorRow = Math.Clamp(source.CursorRow, 0, rows - 1);
        target.CursorColumn = Math.Clamp(source.CursorColumn, 0, columns - 1);
        return target;
    }

    private void ReflowMain(int columns, int rows)
    {
        var allLines = _scrollback.Concat(_main.Lines).Select(line => line.Clone()).ToList();
        var cursorAbsolute = _scrollback.Count + _main.CursorRow;
        var logicalLines = new List<List<TerminalLine>>();
        var current = new List<TerminalLine>();
        foreach (var line in allLines)
        {
            current.Add(line);
            if (!line.IsSoftWrapped)
            {
                logicalLines.Add(current);
                current = [];
            }
        }
        if (current.Count > 0)
        {
            logicalLines.Add(current);
        }

        var output = new List<TerminalLine>();
        var newCursorAbsolute = 0;
        var newCursorColumn = 0;
        var logicalStart = 0;
        foreach (var logical in logicalLines)
        {
            var cells = new List<TerminalCell>();
            var cursorIndex = -1;
            for (var index = 0; index < logical.Count; index++)
            {
                if (logicalStart + index == cursorAbsolute)
                {
                    cursorIndex = cells.Count + _main.CursorColumn;
                }
                cells.AddRange(logical[index].Cells.Select(cell => cell.Clone()));
            }
            var lastContent = cells.FindLastIndex(cell => !cell.IsContinuation && cell.Text.Length > 0);
            var keep = Math.Max(lastContent + 1, cursorIndex + 1);
            if (cells.Count > keep)
            {
                cells.RemoveRange(keep, cells.Count - keep);
            }
            if (cells.Count == 0)
            {
                cells.Add(new TerminalCell());
            }

            var metadata = logical.FirstOrDefault(item => item.LogicalLineNumber is not null);
            var outputStart = output.Count;
            var line = new TerminalLine(columns) { LogicalLineNumber = metadata?.LogicalLineNumber, FirstOutputAtUtc = metadata?.FirstOutputAtUtc };
            var column = 0;
            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                if (cell.IsContinuation)
                {
                    continue;
                }
                var wide = index + 1 < cells.Count && cells[index + 1].IsContinuation;
                if (wide && column == columns - 1)
                {
                    line.IsSoftWrapped = true;
                    output.Add(line);
                    line = new TerminalLine(columns) { LogicalLineNumber = metadata?.LogicalLineNumber, FirstOutputAtUtc = metadata?.FirstOutputAtUtc };
                    column = 0;
                }
                line.Cells[column++] = cell;
                if (wide)
                {
                    line.Cells[column++] = cells[++index];
                }
                if (column == columns && index < cells.Count - 1)
                {
                    line.IsSoftWrapped = true;
                    output.Add(line);
                    line = new TerminalLine(columns) { LogicalLineNumber = metadata?.LogicalLineNumber, FirstOutputAtUtc = metadata?.FirstOutputAtUtc };
                    column = 0;
                }
            }
            output.Add(line);
            if (cursorIndex >= 0)
            {
                newCursorAbsolute = outputStart + Math.Min(cursorIndex / columns, output.Count - outputStart - 1);
                newCursorColumn = Math.Min(cursorIndex % columns, columns - 1);
            }
            logicalStart += logical.Count;
        }
        if (output.Count == 0)
        {
            output.Add(new TerminalLine(columns));
        }

        var start = Math.Max(0, output.Count - rows);
        ClearHistory();
        if (_maxScrollbackLines > 0 && start > 0)
        {
            _scrollback.AddRange(output.Take(start).Skip(Math.Max(0, start - _maxScrollbackLines)));
            _scrollbackBytes = _scrollback.Sum(line => line.EstimatedStorageBytes);
            TrimHistory();
        }
        _main = new TerminalBuffer(columns, rows);
        _main.Lines.Clear();
        _main.Lines.AddRange(output.Skip(start).Take(rows));
        while (_main.Lines.Count < rows)
        {
            _main.Lines.Add(new TerminalLine(columns));
        }
        _main.CursorRow = Math.Clamp(newCursorAbsolute - start, 0, rows - 1);
        _main.CursorColumn = newCursorColumn;
        _main.WrapPending = false;
    }

    private static TerminalLineSnapshot CreateLineSnapshot(TerminalLine line)
    {
        var runs = new List<TerminalRunSnapshot>();
        var clusters = new List<TerminalClusterSnapshot>();
        StringBuilder? text = null;
        TerminalStyle style = default;
        var start = 0;
        for (var column = 0; column < line.Cells.Length; column++)
        {
            var cell = line.Cells[column];
            if (cell.IsContinuation)
            {
                continue;
            }
            var cellText = string.IsNullOrEmpty(cell.Text) ? " " : cell.Text;
            clusters.Add(new TerminalClusterSnapshot
            {
                StartColumn = column,
                ColumnWidth = Math.Max(1, cell.ColumnWidth),
                Text = cellText,
                IsOccupied = cell.Text.Length > 0,
                Foreground = cell.Style.Foreground,
                Background = cell.Style.Background,
                Bold = cell.Style.Bold,
                Underline = cell.Style.Underline,
                Inverse = cell.Style.Inverse,
                Hyperlink = cell.Style.Hyperlink
            });
            if (text is null)
            {
                text = new StringBuilder();
                start = column;
                style = cell.Style;
            }
            else if (cell.Style != style)
            {
                runs.Add(CreateRun(start, text.ToString(), style));
                text.Clear();
                start = column;
                style = cell.Style;
            }
            text.Append(cellText);
        }
        if (text is not null)
        {
            runs.Add(CreateRun(start, text.ToString(), style));
        }
        return new TerminalLineSnapshot
        {
            LineId = line.Id,
            LogicalLineNumber = line.LogicalLineNumber,
            FirstOutputAtUtc = line.FirstOutputAtUtc,
            Text = string.Concat(runs.Select(run => run.Text)),
            IsSoftWrapped = line.IsSoftWrapped,
            Runs = runs,
            Clusters = clusters
        };
    }

    private static TerminalLogicalLineSnapshot CreateLogicalLine(TerminalLine line) => new()
    {
        LineId = line.Id,
        Text = CreateLogicalText(line),
        IsSoftWrapped = line.IsSoftWrapped,
        Clusters = CreateLineSnapshot(line).Clusters
    };

    private static string CreateLogicalText(TerminalLine line)
    {
        var lastOccupied = Array.FindLastIndex(line.Cells, cell => !cell.IsContinuation && cell.Text.Length > 0);
        var text = new StringBuilder();
        for (var column = 0; column <= lastOccupied; column++)
        {
            var cell = line.Cells[column];
            if (!cell.IsContinuation)
            {
                text.Append(cell.Text.Length == 0 ? " " : cell.Text);
            }
        }
        return text.ToString();
    }

    private static TerminalRunSnapshot CreateRun(int start, string text, TerminalStyle style) => new()
    {
        StartColumn = start,
        Text = text,
        Foreground = style.Foreground,
        Background = style.Background,
        Bold = style.Bold,
        Underline = style.Underline,
        Inverse = style.Inverse
    };

    private void MarkDirty(int start, int end)
    {
        _dirtyStart = Math.Min(_dirtyStart, start);
        _dirtyEnd = Math.Max(_dirtyEnd, end);
    }

    private void MarkAllDirty() => MarkDirty(0, Rows - 1);
}
