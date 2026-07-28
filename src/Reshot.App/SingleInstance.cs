using System.Threading;
using Reshot.Core.Diagnostics;

namespace Reshot.App;

/// <summary>
/// Enforces one running reshot per user session (SPEC §14). Uses a named mutex
/// to detect a prior instance and a named auto-reset event to wake it: a second
/// launch signals the event and exits, the first instance handles the signal via
/// an OS-level registered wait — event-driven, no polling, no background CPU.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // Per-user names (Local\ namespace) so different accounts don't collide.
    private const string MutexName = @"Local\reshot.singleinstance.mutex";
    private const string EventName = @"Local\reshot.singleinstance.signal";

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private RegisteredWaitHandle? _registeredWait;
    private bool _disposed;

    /// <summary>Raised on a thread-pool thread when another instance was launched.</summary>
    public event EventHandler? SecondInstanceLaunched;

    /// <summary>
    /// Returns true if this is the primary instance. If false, it has already
    /// signalled the existing instance and the caller should exit immediately.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var isNew);

        if (!isNew)
        {
            SignalExistingInstance();
            return false;
        }

        RegisterSignalListener();
        return true;
    }

    private static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var existing))
            {
                using (existing)
                    existing.Set();
                Log.Info("SingleInstance: signalled the running instance.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("SingleInstance: failed to signal existing instance", ex);
        }
    }

    private void RegisterSignalListener()
    {
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        // OS-level wait: costs no dedicated thread and no polling. The callback
        // fires only when a second instance sets the event.
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _signal,
            (_, _) => SecondInstanceLaunched?.Invoke(this, EventArgs.Empty),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _registeredWait?.Unregister(null);
        _signal?.Dispose();

        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
            _mutex.Dispose();
        }
    }
}
