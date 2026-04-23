// Manifold — SteamMultiplayerPeer
// Godot MultiplayerPeerExtension backed by Steam ISteamNetworkingSockets P2P.
// (MASTER_DESIGN §8.10)
// Tasks 12-14 add CreateHost/CreateClient/Poll/Put/Get to this partial class.

using Godot;
using System.Collections.Generic;
using Manifold.Core;
using Manifold.Core.Networking;
using Manifold.Core.Testing;

namespace Manifold.Godot.Networking;

/// <summary>
/// A Godot <see cref="MultiplayerPeerExtension"/> implementation backed by Steam's
/// <c>ISteamNetworkingSockets</c> P2P networking layer.
/// (MASTER_DESIGN §8.10)
/// </summary>
public partial class SteamMultiplayerPeer : MultiplayerPeerExtension
{
    // ── Internal connection state machine ────────────────────────────────────

    /// <summary>Internal state of this peer's connection lifecycle.</summary>
    private enum PeerState
    {
        /// <summary>Not yet initialized.</summary>
        Idle,
        /// <summary>Host: listen socket created, waiting for incoming connections.</summary>
        Listening,
        /// <summary>Client: outgoing connection initiated.</summary>
        Connecting,
        /// <summary>Steam connection established; waiting for handshake completion.</summary>
        Authenticating,
        /// <summary>Fully connected and ready for data exchange.</summary>
        Connected,
        /// <summary>Graceful disconnect initiated; draining in-flight packets.</summary>
        Disconnecting,
        /// <summary>Connection closed. LastDisconnectInfo is populated.</summary>
        Disconnected,
    }

    private PeerState _state = PeerState.Idle;

    // ── Core references ───────────────────────────────────────────────────────

    private readonly SteamNetworkingCore _core;
    private readonly PeerIdMapper _peerMapper = new();

    // ── Identity ──────────────────────────────────────────────────────────────

    private int  _uniqueId;
    private bool _isServer;

    // ── Transfer config ───────────────────────────────────────────────────────

    private int _transferChannel;
    private MultiplayerPeer.TransferModeEnum _transferMode = MultiplayerPeer.TransferModeEnum.Reliable;
    private bool _unreliableOrderedWarned;  // fires the one-time warning at most once per peer instance

    private int  _targetPeer;
    private bool _refusingNewConnections;

    // ── Steam send flags (per MASTER_DESIGN §8.2) ────────────────────────────

    /// <summary>
    /// When <c>true</c>, disables Nagle's algorithm — messages sent immediately.
    /// See MASTER_DESIGN §8.3 for NoNagle vs NoDelay semantics.
    /// </summary>
    public bool NoNagle { get; set; }

    /// <summary>
    /// When <c>true</c>, messages that cannot be placed on the wire within ~200 ms are
    /// silently dropped. Implies NoNagle. Use for real-time positional state.
    /// See MASTER_DESIGN §8.3 for NoNagle vs NoDelay semantics.
    /// </summary>
    public bool NoDelay { get; set; }

    /// <summary>When <c>true</c>, use Steam's relay network. Default: <c>true</c>.</summary>
    public bool UseRelay { get; set; } = true;

    // ── Incoming packet queue ─────────────────────────────────────────────────

    private readonly Queue<ReceivedPacket> _incoming = new();
    private ReceivedPacket _currentPacket;  // set during _GetPacket, used by _GetPacketPeer etc.

    // ── Disconnect info ───────────────────────────────────────────────────────

    /// <summary>
    /// Information about the most recent disconnection.
    /// Populated when transitioning to <see cref="PeerState.Disconnected"/>.
    /// <c>null</c> if still connected or never connected.
    /// </summary>
    public DisconnectInfo? LastDisconnectInfo { get; private set; }

    /// <summary>
    /// Emitted when a peer disconnects, with additional detail beyond the standard
    /// <see cref="MultiplayerPeer.PeerDisconnected"/> signal.
    /// (MASTER_DESIGN §8.8)
    /// </summary>
    [Signal]
    public delegate void PeerDisconnectedWithReasonEventHandler(
        int peerId, int code, string reason, bool wasLocal);

    // ── Constructor ───────────────────────────────────────────────────────────

    public SteamMultiplayerPeer()
    {
        // Uses FakeSteamBackend via INetworkingBackend; production uses LiveSteamNetworkingBackend.
        // For now, we allow injection via the constructor overload below for tests.
        _core = new SteamNetworkingCore(new FakeSteamBackend());
        // TODO (Task 17): Register with SteamPeerRegistry for lifecycle hook
    }

    // ── Constructor for injection (tests / advanced use) ─────────────────────

    /// <summary>Creates a peer backed by the given networking backend (for testing).</summary>
    internal SteamMultiplayerPeer(INetworkingBackend backend)
    {
        _core = new SteamNetworkingCore(backend);
    }

    // ── MultiplayerPeerExtension overrides ────────────────────────────────────

    /// <inheritdoc/>
    public override MultiplayerPeer.ConnectionStatus _GetConnectionStatus() => _state switch
    {
        PeerState.Idle
            or PeerState.Listening
            or PeerState.Connecting
            or PeerState.Authenticating  => MultiplayerPeer.ConnectionStatus.Connecting,
        PeerState.Connected
            or PeerState.Disconnecting   => MultiplayerPeer.ConnectionStatus.Connected,
        _                                => MultiplayerPeer.ConnectionStatus.Disconnected,
    };

    /// <inheritdoc/>
    public override bool _IsServer() => _isServer;

    /// <inheritdoc/>
    public override bool _IsServerRelaySupported() => true;

    /// <inheritdoc/>
    public override int _GetUniqueId() => _uniqueId;

    /// <inheritdoc/>
    public override bool _IsRefusingNewConnections() => _refusingNewConnections;

    /// <inheritdoc/>
    public override void _SetRefuseNewConnections(bool pEnable) => _refusingNewConnections = pEnable;

    /// <inheritdoc/>
    public override int _GetTransferChannel() => _transferChannel;

    /// <inheritdoc/>
    public override void _SetTransferChannel(int pChannel) => _transferChannel = pChannel;

    /// <inheritdoc/>
    public override MultiplayerPeer.TransferModeEnum _GetTransferMode() => _transferMode;

    /// <inheritdoc/>
    public override void _SetTransferMode(MultiplayerPeer.TransferModeEnum pMode)
    {
        if (pMode == MultiplayerPeer.TransferModeEnum.UnreliableOrdered && !_unreliableOrderedWarned)
        {
            _unreliableOrderedWarned = true;
            GD.PushWarning(
                "[Manifold] WARNING: TransferMode.UnreliableOrdered is set on SteamMultiplayerPeer, " +
                "but Steam has no native ordered-unreliable transport. Manifold is using unordered " +
                "unreliable delivery. In v1, Manifold does not emulate ordered-unreliable delivery. " +
                "Applications requiring stale-packet suppression or monotonic snapshot delivery must " +
                "implement sequence handling above the transport layer.");
        }
        _transferMode = pMode;
    }

    /// <inheritdoc/>
    public override void _SetTargetPeer(int pPeer) => _targetPeer = pPeer;

    /// <inheritdoc/>
    public override int _GetMaxPacketSize() => 1_048_576;

    /// <inheritdoc/>
    public override int _GetAvailablePacketCount() => _incoming.Count;
}
