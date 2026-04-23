// Manifold — SteamPeerRegistry
// Tracks active ISteamPeer instances via WeakReferences for lifecycle shutdown.
// (MASTER_DESIGN §4 Shutdown Contract Step 3)

using System;
using System.Collections.Generic;

namespace Manifold.Core.Dispatch;

/// <summary>
/// Registry of active <see cref="ISteamPeer"/> instances.
/// <see cref="Manifold.Core.Lifecycle.SteamLifecycle.Dispose"/> calls <see cref="ShutdownAll"/>
/// to cleanly transition all peers to Disconnected state (shutdown contract step 3).
/// (MASTER_DESIGN §4)
/// </summary>
internal static class SteamPeerRegistry
{
    // WeakReference avoids keeping peers alive past their natural lifetime.
    private static readonly List<WeakReference<ISteamPeer>> _peers = new();
    private static readonly object _lock = new();

    /// <summary>Registers a peer for shutdown notification.</summary>
    internal static void Register(ISteamPeer peer)
    {
        lock (_lock)
            _peers.Add(new WeakReference<ISteamPeer>(peer));
    }

    /// <summary>Unregisters a peer (call from peer's Close/Dispose).</summary>
    internal static void Unregister(ISteamPeer peer)
    {
        lock (_lock)
            _peers.RemoveAll(wr => !wr.TryGetTarget(out var p) || ReferenceEquals(p, peer));
    }

    /// <summary>
    /// Forces <see cref="ISteamPeer.ForceDisconnect"/> on all registered live peers.
    /// Called by <see cref="Manifold.Core.Lifecycle.SteamLifecycle.Dispose"/> at shutdown step 3.
    /// Dead weak references are silently skipped.
    /// </summary>
    internal static void ShutdownAll()
    {
        List<ISteamPeer> alive;
        lock (_lock)
        {
            alive = new List<ISteamPeer>();
            foreach (var wr in _peers)
                if (wr.TryGetTarget(out var p)) alive.Add(p);
            _peers.Clear();
        }
        foreach (var peer in alive)
        {
            try { peer.ForceDisconnect(); }
            catch { /* never let a peer's disconnect crash the shutdown sequence */ }
        }
    }

    /// <summary>Resets all registrations. For testing only.</summary>
    internal static void ResetForTesting()
    {
        lock (_lock) _peers.Clear();
    }
}
