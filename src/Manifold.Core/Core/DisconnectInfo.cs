// Manifold — DisconnectInfo
// Carries the reason a Steam networking peer disconnected.

namespace Manifold.Core;

/// <summary>
/// Describes why a Steam networking connection was closed.
/// Populated on the <see cref="Networking.SteamNetworkingCore"/> peer when a
/// connection transitions to the <c>Disconnected</c> state.
/// </summary>
public readonly record struct DisconnectInfo
{
    /// <summary>
    /// The <c>ESteamNetConnectionEnd</c> reason code provided by Steam.
    /// Values below 1000 are application-defined; values above are Steam-defined.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// Human-readable debug string provided by Steam describing the disconnect reason.
    /// Suitable for logging; not intended for display to end users.
    /// </summary>
    public string Reason { get; init; }

    /// <summary>
    /// <c>true</c> if the local side initiated the close (via <c>Close()</c> or
    /// <c>DisconnectPeer()</c>); <c>false</c> if the remote side closed or a
    /// network-level failure occurred.
    /// </summary>
    public bool WasLocalClose { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"DisconnectInfo(Code={Code}, Local={WasLocalClose}, Reason=\"{Reason}\")";
}
