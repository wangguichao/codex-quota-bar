namespace CodexQuotaBar;

/// <summary>The restore event is prepared before the mutex so early duplicate launches cannot lose requests.</summary>
internal sealed class InstanceRestoreSignal : IDisposable
{
    private readonly EventWaitHandle signal;
    private readonly RegisteredWaitHandle listener;
    private readonly bool ownsSignal;
    internal static EventWaitHandle Prepare(string name) => new(false, EventResetMode.AutoReset, name);
    internal InstanceRestoreSignal(string name, Action restore) : this(Prepare(name), restore, true) { }
    internal InstanceRestoreSignal(EventWaitHandle signal, Action restore) : this(signal, restore, false) { }
    private InstanceRestoreSignal(EventWaitHandle signal, Action restore, bool ownsSignal)
    {
        this.signal = signal; this.ownsSignal = ownsSignal;
        listener = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => restore(), null, Timeout.Infinite, false);
    }
    internal static bool Send(string name)
    {
        try { using var signal = EventWaitHandle.OpenExisting(name); return signal.Set(); }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }
    public void Dispose() { listener.Unregister(null); if (ownsSignal) signal.Dispose(); }
}

