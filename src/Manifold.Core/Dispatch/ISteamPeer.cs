// Manifold — ISteamPeer interface
// Minimal peer interface used by SteamPeerRegistry for lifecycle shutdown.
// (MASTER_DESIGN §4 Shutdown Contract Step 3)

namespace Manifold.Core.Dispatch;

/// <summary>
/// Minimal interface for <see cref="SteamPeerRegistry"/> to call
/// <see cref="ForceDisconnect"/> on all active peers during <c>SteamLifecycle.Dispose()</c>.
/// Implemented by <c>Manifold.Godot.Networking.SteamMultiplayerPeer</c>.
/// (MASTER_DESIGN §4 Shutdown Contract Step 3)
/// </summary>
public interface ISteamPeer
{
    /// <summary>
    /// Forces an immediate disconnection. Called during Steam shutdown.
    /// Implementations must emit <c>PeerDisconnected</c> for each connected peer
    /// before this method returns.
    /// </summary>
    void ForceDisconnect();
}
