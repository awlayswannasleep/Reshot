using Reshot.Core.Session;
using Xunit;

namespace Reshot.Core.Tests;

public class SessionStateMachineTests
{
    [Fact]
    public void Starts_idle()
    {
        var sm = new SessionStateMachine();
        Assert.Equal(SessionState.Idle, sm.State);
    }

    [Fact]
    public void Walks_the_happy_path()
    {
        var sm = new SessionStateMachine();
        Assert.True(sm.TryTransition(SessionState.Capturing));
        Assert.True(sm.TryTransition(SessionState.Selecting));
        Assert.True(sm.TryTransition(SessionState.Editing));
        Assert.True(sm.TryTransition(SessionState.Exporting));
        Assert.True(sm.TryTransition(SessionState.Idle));
        Assert.Equal(SessionState.Idle, sm.State);
    }

    [Fact]
    public void Rejects_illegal_transition()
    {
        var sm = new SessionStateMachine();
        // Can't jump straight from Idle to Editing.
        Assert.False(sm.TryTransition(SessionState.Editing));
        Assert.Equal(SessionState.Idle, sm.State);
    }

    [Fact]
    public void Editing_toggles_back_to_selecting()
    {
        var sm = new SessionStateMachine();
        sm.TryTransition(SessionState.Capturing);
        sm.TryTransition(SessionState.Selecting);
        sm.TryTransition(SessionState.Editing);
        Assert.True(sm.TryTransition(SessionState.Selecting));
        Assert.Equal(SessionState.Selecting, sm.State);
    }

    [Fact]
    public void Editing_can_enter_recording_stub()
    {
        var sm = new SessionStateMachine();
        sm.TryTransition(SessionState.Capturing);
        sm.TryTransition(SessionState.Selecting);
        sm.TryTransition(SessionState.Editing);
        Assert.True(sm.TryTransition(SessionState.Recording));
        Assert.True(sm.TryTransition(SessionState.Idle));
    }

    [Fact]
    public void Reset_forces_idle_and_fires_change()
    {
        var sm = new SessionStateMachine();
        sm.TryTransition(SessionState.Capturing);

        SessionState? from = null, to = null;
        sm.Changed += (f, t) => { from = f; to = t; };
        sm.Reset();

        Assert.Equal(SessionState.Idle, sm.State);
        Assert.Equal(SessionState.Capturing, from);
        Assert.Equal(SessionState.Idle, to);
    }

    [Fact]
    public void Same_state_transition_is_noop_true()
    {
        var sm = new SessionStateMachine();
        Assert.True(sm.TryTransition(SessionState.Idle));
    }
}
