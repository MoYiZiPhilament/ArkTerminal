using System.Diagnostics;
using System.Numerics;
using ArkTerminal.Terminal.Rendering;

namespace ArkTerminal.Rendering;

internal sealed class TerminalCursorMotion
{
    private CursorView? _previous;
    private Vector2 _from;
    private Vector2 _target;
    private long _startedAt;

    public bool IsAnimating { get; private set; }
    public bool TargetChanged { get; private set; }

    public Vector2 Update(TerminalScreenSnapshot snapshot, long timestamp, bool enabled)
    {
        TargetChanged = false;
        var target = new Vector2(snapshot.CursorColumn, snapshot.CursorRow);
        var current = new CursorView(snapshot);
        var targetChanged = _target != target;
        var position = IsAnimating ? Evaluate(timestamp) : _target;
        if (IsAnimating && !enabled)
        {
            IsAnimating = false;
            position = _target;
        }

        var sameView = _previous is not null &&
            _previous.SessionId == snapshot.SessionId &&
            _previous.IsAlternateScreen == snapshot.IsAlternateScreen &&
            _previous.ScrollbackOffset == snapshot.ScrollbackOffset &&
            _previous.Columns == snapshot.Columns && _previous.Rows == snapshot.Rows;
        var canAnimate = enabled && snapshot.CursorVisible && _previous is { CursorVisible: true } && sameView &&
            Math.Abs(target.X - position.X) <= 12 && Math.Abs(target.Y - position.Y) <= 1;

        if (targetChanged || !sameView)
        {
            TargetChanged = _previous is not null;
            _from = canAnimate ? position : target;
            _target = target;
            _startedAt = timestamp;
            IsAnimating = canAnimate && _from != target;
            position = IsAnimating ? _from : target;
        }
        else if (IsAnimating)
        {
            position = Evaluate(timestamp);
        }
        else
        {
            position = target;
        }

        if (!snapshot.CursorVisible)
        {
            IsAnimating = false;
            position = target;
        }
        _previous = current;
        _target = target;
        return position;
    }

    public void Reset()
    {
        _previous = null;
        _from = _target = Vector2.Zero;
        _startedAt = 0;
        IsAnimating = false;
        TargetChanged = false;
    }

    private Vector2 Evaluate(long timestamp)
    {
        var progress = Math.Clamp((float)(Stopwatch.GetElapsedTime(_startedAt, timestamp).TotalSeconds / 0.09), 0, 1);
        if (progress >= 1)
        {
            IsAnimating = false;
            return _target;
        }
        var eased = 1 - MathF.Pow(1 - progress, 3);
        return Vector2.Lerp(_from, _target, eased);
    }

    private sealed record CursorView(TerminalScreenSnapshot Snapshot)
    {
        public Guid SessionId => Snapshot.SessionId;
        public bool CursorVisible => Snapshot.CursorVisible;
        public bool IsAlternateScreen => Snapshot.IsAlternateScreen;
        public int ScrollbackOffset => Snapshot.ScrollbackOffset;
        public int Columns => Snapshot.Columns;
        public int Rows => Snapshot.Rows;
    }
}
