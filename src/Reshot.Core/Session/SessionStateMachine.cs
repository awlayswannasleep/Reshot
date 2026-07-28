using Reshot.Core.Diagnostics;

namespace Reshot.Core.Session;

/// <summary>States of a capture session (ARCHITECTURE §8).</summary>
public enum SessionState
{
    /// <summary>Asleep in the tray; no overlay.</summary>
    Idle,

    /// <summary>Freezing the screen (taking the snapshot).</summary>
    Capturing,

    /// <summary>Overlay is up, no active selection yet.</summary>
    Selecting,

    /// <summary>A selection exists and is being edited (move/resize/draw later).</summary>
    Editing,

    /// <summary>Producing output (Copy / Save / Save As).</summary>
    Exporting,

    /// <summary>Live video recording. Phase 6, defined now as a stub target.</summary>
    Recording,
}

/// <summary>
/// The session finite-state machine from ARCHITECTURE §8. Guards transitions so
/// the app can't slip into an illegal state, and logs every move. Recording is a
/// declared-but-unentered stub until Phase 6.
/// </summary>
public sealed class SessionStateMachine
{
    private static readonly Dictionary<SessionState, SessionState[]> Allowed = new()
    {
        [SessionState.Idle] = new[] { SessionState.Capturing },
        [SessionState.Capturing] = new[] { SessionState.Selecting, SessionState.Idle },
        [SessionState.Selecting] = new[] { SessionState.Editing, SessionState.Exporting, SessionState.Idle },
        [SessionState.Editing] = new[]
        {
            SessionState.Selecting, SessionState.Exporting, SessionState.Recording, SessionState.Idle,
        },
        [SessionState.Exporting] = new[] { SessionState.Idle },
        [SessionState.Recording] = new[] { SessionState.Idle },
    };

    public SessionState State { get; private set; } = SessionState.Idle;

    /// <summary>Raised after a successful transition: (from, to).</summary>
    public event Action<SessionState, SessionState>? Changed;

    /// <summary>Whether <paramref name="to"/> is reachable from the current state.</summary>
    public bool CanTransition(SessionState to) =>
        State == to || (Allowed.TryGetValue(State, out var next) && Array.IndexOf(next, to) >= 0);

    /// <summary>Attempts a transition; returns false (and logs) if it is not allowed.</summary>
    public bool TryTransition(SessionState to)
    {
        if (State == to)
            return true;

        if (!CanTransition(to))
        {
            Log.Warn($"Session: illegal transition {State} → {to} ignored.");
            return false;
        }

        var from = State;
        State = to;
        Log.Info($"Session: {from} → {to}.");
        Changed?.Invoke(from, to);
        return true;
    }

    /// <summary>Forces the session back to <see cref="SessionState.Idle"/> (teardown).</summary>
    public void Reset()
    {
        if (State == SessionState.Idle)
            return;

        var from = State;
        State = SessionState.Idle;
        Log.Info($"Session: {from} → Idle (reset).");
        Changed?.Invoke(from, SessionState.Idle);
    }
}
