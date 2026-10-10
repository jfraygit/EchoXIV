using System;

namespace EchoMix.Plugin.UI.Screens;

/// The re-fetch clock for a Browse list that has to keep itself current.
internal sealed class RelayPoll
{
    /// How long between automatic re-fetches.
    private const long IntervalMs = 15_000;

    private long lastSent;
    private bool everSent;

    /// True the first time this is asked and every IntervalMs after, stamping the clock as it says yes - so
    /// the caller can ask once per frame and let this decide.
    public bool Due(bool canSend)
    {
        if (!canSend)
            return false;

        var now = Environment.TickCount64;
        if (everSent && now - lastSent < IntervalMs)
            return false;

        everSent = true;
        lastSent = now;
        return true;
    }

    /// Restarts the interval without asking - for a manual Refresh press, which is never throttled (a press
    /// is someone asking for the current answer, and handing them the cached one is the bug they pressed it
    /// about) but should still push the next automatic fetch out a full interval rather than letting one land
    /// a frame later.
    public void Stamp()
    {
        everSent = true;
        lastSent = Environment.TickCount64;
    }
}
