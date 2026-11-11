using System.Diagnostics;
using System.Numerics;
using ArkTerminal.Terminal.Rendering;

namespace ArkTerminal.Rendering;

internal sealed class TerminalCursorMotion
{
    private const double Tau = 0.035;
    private CursorView? _previous;
    private Vector2 _position;
    private Vector2 _target;
    private long _lastTimestamp;

    public bool IsAnimating { get; private set; }
    public bool TargetChanged { get; private set; }

    public Vector2 Update(TerminalScreenSnapshot snapshot, long timestamp, bool enabled)
    {
        TargetChanged = false;
        var target = new Vector2(snapshot.CursorColumn, snapshot.CursorRow);
        var current = new CursorView(snapshot);
        var targetChanged = _target != target;
        var sameView = _previous is not null &&
            _previous.SessionId == snapshot.SessionId &&
            _previous.IsAlternateScreen == snapshot.IsAlternateScreen &&
            _previous.ScrollbackOffset == snapshot.ScrollbackOffset &&
            _previous.Columns == snapshot.Columns && _previous.Rows == snapshot.Rows;
        var canAnimate = enabled && snapshot.CursorVisible && _previous is { CursorVisible: true } && sameView &&
            Math.Abs(target.X - _position.X) <= 12 && Math.Abs(target.Y - _position.Y) <= 1;
        _target = target;

        if (targetChanged || !sameView)
        {
            TargetChanged = _previous is not null;
            IsAnimating = canAnimate && _position != target;
            if (!canAnimate) _position = target;
        }

        if (!enabled || !snapshot.CursorVisible)
        {
            IsAnimating = false;
            _position = target;
        }
        else if (IsAnimating)
        {
            // Frame-rate-independent exponential approach: a moving target is chased
            // smoothly instead of restarting a fixed-duration ease on every output batch.
            var dt = Math.Min(Stopwatch.GetElapsedTime(_lastTimestamp, timestamp).TotalSeconds, 0.1);
            if (dt > 0)
            {
                var alpha = 1 - MathF.Exp((float)(-dt / Tau));
                _position = Vector2.Lerp(_position, _target, alpha);
            }
            if ((_target - _position).Length() < 0.01f)
            {
                _position = _target;
                IsAnimating = false;
            }
        }
        else
        {
            _position = target;
        }

        _previous = current;
        _lastTimestamp = timestamp;
        return _position;
    }

    public void Reset()
    {
        _previous = null;
        _position = _target = Vector2.Zero;
        _lastTimestamp = 0;
        IsAnimating = false;
        TargetChanged = false;
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