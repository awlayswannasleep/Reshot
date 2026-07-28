namespace Reshot.Capture;

/// <summary>
/// The capture boundary from ARCHITECTURE §9: today a one-shot
/// <see cref="SnapshotAllMonitors"/>; Phase 6 adds StartStream for video on the
/// same abstraction so the rest of the app never touches capture internals.
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>
    /// Grabs a single frozen frame of every monitor and composes them into one
    /// virtual-desktop bitmap. Excludes the cursor. Throws on failure.
    /// </summary>
    CapturedFrame SnapshotAllMonitors();

    /// <summary>
    /// Starts a continuous capture of the monitor that contains the given
    /// virtual-screen point (Phase 6, video). The caller crops the recording
    /// region from the returned stream and disposes it to stop.
    /// </summary>
    WgcMonitorStream StartMonitorStream(int screenX, int screenY);
}
