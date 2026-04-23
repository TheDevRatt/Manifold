// Manifold — SteamMultiplayerPeer
// Godot MultiplayerPeerExtension backed by Steam ISteamNetworkingSockets P2P.
// (MASTER_DESIGN §8.10)
// Tasks 12-14 add CreateHost/CreateClient/Poll/Put/Get to this partial class.

using Godot;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Manifold.Core;
using Manifold.Core.Interop;
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
    private ReceivedPacket _currentPacket;  // set during _GetPacketScript, used by _GetPacketPeer etc.

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

    /// <summary>
    /// Creates a <see cref="SteamMultiplayerPeer"/> using a stub backend.
    /// Production use requires the live Steam backend to be wired in (see TODO below).
    /// Use the internal <c>(INetworkingBackend)</c> overload for direct test injection.
    /// </summary>
    public SteamMultiplayerPeer()
    {
        _core = new SteamNetworkingCore(new FakeSteamBackend());
        // TODO (Phase 3): wire LiveSteamNetworkingBackend when SteamLifecycle is initialized (replace FakeSteamBackend).
        // TODO (Task 17): Register with SteamPeerRegistry for lifecycle hook.

        // Subscribe to core events
        _core.IncomingConnection      += OnIncomingConnection;
        _core.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    // ── Constructor for injection (tests / advanced use) ─────────────────────

    /// <summary>Creates a peer backed by the given networking backend (for testing).</summary>
    internal SteamMultiplayerPeer(INetworkingBackend backend)
    {
        _core = new SteamNetworkingCore(backend);

        // Subscribe to core events
        _core.IncomingConnection      += OnIncomingConnection;
        _core.ConnectionStatusChanged += OnConnectionStatusChanged;
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
    /// <remarks>
    /// <see cref="MultiplayerPeer.TransferModeEnum.UnreliableOrdered"/> has no native Steam equivalent
    /// and is silently degraded to unordered unreliable with a one-time editor warning.
    /// (MASTER_DESIGN §8.2)
    /// </remarks>
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

    // ── Host / Client creation (MASTER_DESIGN §8.10) ──────────────────────────

    /// <summary>
    /// Creates a P2P listen socket. Server peer ID is always 1.
    /// Transitions state to <see cref="PeerState.Listening"/>.
    /// </summary>
    /// <param name="virtualPort">Virtual port to listen on. Must match the port clients connect to.</param>
    /// <returns><see cref="Error.Ok"/> on success; <see cref="Error.AlreadyInUse"/> if already initialised;
    /// <see cref="Error.CantCreate"/> if the listen socket could not be created.</returns>
    public Error CreateHost(int virtualPort = 0)
    {
        if (_state != PeerState.Idle)
            return Error.AlreadyInUse;

        var socket = _core.CreateHost(virtualPort);
        if (!socket.IsValid)
            return Error.CantCreate;

        _isServer = true;
        _uniqueId = 1;  // Server is always peer 1 in Godot
        _state    = PeerState.Listening;
        return Error.Ok;
    }

    /// <summary>
    /// Initiates a P2P connection to the host identified by their Steam64 ID.
    /// Transitions state to <see cref="PeerState.Connecting"/>; the handshake
    /// will transition to <see cref="PeerState.Connected"/> when complete.
    /// </summary>
    /// <param name="hostSteamId">The host's Steam64 ID.</param>
    /// <param name="virtualPort">Virtual port to connect on. Must match the host's listen port.</param>
    /// <returns><see cref="Error.Ok"/> on success; <see cref="Error.AlreadyInUse"/> if already initialised;
    /// <see cref="Error.CantConnect"/> if the connection could not be initiated.</returns>
    public Error CreateClient(SteamId hostSteamId, int virtualPort = 0)
    {
        if (_state != PeerState.Idle)
            return Error.AlreadyInUse;

        var conn = _core.CreateClient(hostSteamId, virtualPort);
        if (!conn.IsValid)
            return Error.CantConnect;

        _isServer = false;
        _uniqueId = 0;  // assigned by server during handshake
        _state    = PeerState.Connecting;
        return Error.Ok;
    }

    // ── Lobby helpers — Phase 3 stubs (MASTER_DESIGN §8.10) ──────────────────

    /// <summary>
    /// Creates a Steam lobby and immediately calls <see cref="CreateHost"/> on success.
    /// <para><b>Phase 3 stub</b>: returns <see cref="Error.Unavailable"/> until <c>SteamMatchmaking</c> is implemented.</para>
    /// </summary>
    /// <param name="type">Lobby visibility (public, friends-only, private, etc.).</param>
    /// <param name="maxMembers">Maximum number of members the lobby will accept.</param>
    /// <param name="cancellationToken">Unused until Phase 3; reserved for future async cancellation.</param>
    /// <returns><see cref="Error.Unavailable"/> always (Phase 3 stub).</returns>
    public Task<Error> HostWithLobbyAsync(
        ELobbyType type,
        int maxMembers,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Error.Unavailable); // cancellationToken intentionally unused until Phase 3

    /// <summary>
    /// Joins a Steam lobby and immediately calls <see cref="CreateClient"/> using the lobby owner's Steam ID.
    /// <para><b>Phase 3 stub</b>: returns <see cref="Error.Unavailable"/> until <c>SteamMatchmaking</c> is implemented.</para>
    /// </summary>
    /// <param name="lobbyId">The Steam64 ID of the lobby to join.</param>
    /// <param name="cancellationToken">Unused until Phase 3; reserved for future async cancellation.</param>
    /// <returns><see cref="Error.Unavailable"/> always (Phase 3 stub).</returns>
    public Task<Error> JoinLobbyAsync(
        SteamId lobbyId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Error.Unavailable); // cancellationToken intentionally unused until Phase 3

    // ── Steam send-flag constants (MASTER_DESIGN §8.2) ────────────────────────

    private const int k_nSteamNetworkingSend_Unreliable        = 0;
    private const int k_nSteamNetworkingSend_NoDelay           = 4;
    private const int k_nSteamNetworkingSend_NoNagle           = 1;
    private const int k_nSteamNetworkingSend_Reliable          = 8;
    private const int k_nSteamNetworkingSend_UnreliableNoDelay = 5; // Unreliable | NoDelay | NoNagle (= 0 | 4 | 1 = 5)

    // ── Steam connection state constants (from steamnetworkingtypes.h) ────────

    private const int k_ESteamNetworkingConnectionState_Connecting             = 1;
    private const int k_ESteamNetworkingConnectionState_Connected              = 3;
    private const int k_ESteamNetworkingConnectionState_ClosedByPeer           = 4;
    private const int k_ESteamNetworkingConnectionState_ProblemDetectedLocally = 6;

    // ── Handshake tracking (Task 14 will fully populate) ─────────────────────

    private readonly System.Collections.Generic.Dictionary<uint, HandshakeState> _pendingHandshakes = new();

    // ── Poll / send / receive / close ─────────────────────────────────────────

    /// <inheritdoc/>
    public override void _Poll()
    {
        if (_state is PeerState.Idle or PeerState.Disconnected) return;

        var pending = new System.Collections.Generic.List<ReceivedPacket>();
        _core.DrainMessages(pending);

        foreach (var pkt in pending)
        {
            switch (pkt.Kind)
            {
                case PacketKind.Data:
                    // Buffer stays alive — payload will be copied in _GetPacketScript
                    _incoming.Enqueue(pkt);
                    break;

                case PacketKind.Handshake when !_isServer:
                    // Client receives peer ID assignment from server
                    int spanLen = pkt.Size > 0 ? pkt.Size : pkt.Buffer.Length;
                    if (HandshakeProtocol.TryParseHandshake(pkt.Buffer.AsSpan(0, spanLen), out int peerId))
                    {
                        _uniqueId = peerId;
                        var ack = HandshakeProtocol.BuildAck();
                        _core.SendTo(_core.ServerConnection, ack, k_nSteamNetworkingSend_Reliable);
                        _state = PeerState.Connected;
                        EmitSignal(MultiplayerPeer.SignalName.PeerConnected, 1L);
                    }
                    System.Buffers.ArrayPool<byte>.Shared.Return(pkt.Buffer);
                    break;

                case PacketKind.HandshakeAck when _isServer:
                    if (_pendingHandshakes.TryGetValue(pkt.Connection, out var hs))
                    {
                        hs.MarkComplete();
                        _pendingHandshakes.Remove(pkt.Connection);
                        if (_peerMapper.TryGetGodotId(pkt.Connection, out int clientGodotId))
                        {
                            // Both sides now connected
                            EmitSignal(MultiplayerPeer.SignalName.PeerConnected, (long)clientGodotId);
                        }
                    }
                    System.Buffers.ArrayPool<byte>.Shared.Return(pkt.Buffer);
                    break;

                case PacketKind.Disconnect:
                    HandleRemoteDisconnect(pkt.Connection);
                    System.Buffers.ArrayPool<byte>.Shared.Return(pkt.Buffer);
                    break;

                default:
                    System.Buffers.ArrayPool<byte>.Shared.Return(pkt.Buffer);
                    break;
            }
        }

        CheckHandshakeTimeouts();
    }

    /// <inheritdoc/>
    public override Error _PutPacketScript(byte[] pBuffer)
    {
        if (_state != PeerState.Connected) return Error.Unavailable;

        int sendFlags = ComputeSendFlags();

        // Build: 2-byte Manifold header + payload
        int totalSize = PacketHeader.Size + pBuffer.Length;
        byte[] outBuf = new byte[totalSize]; // TODO: optimize with ArrayPool for hot path
        new PacketHeader(PacketKind.Data, (byte)_transferChannel).Encode(outBuf.AsSpan());
        pBuffer.AsSpan().CopyTo(outBuf.AsSpan(PacketHeader.Size));

        if (_targetPeer <= 0)
        {
            // 0 = broadcast; negative = all-except-one
            foreach (var (conn, godotId) in _peerMapper.GetAllConnections())
            {
                if (_targetPeer == 0 || godotId != -_targetPeer)
                    _core.SendTo(conn, outBuf, sendFlags);
            }
        }
        else
        {
            uint conn = GetConnectionForPeer(_targetPeer);
            if (conn == 0) return Error.InvalidParameter;
            _core.SendTo(conn, outBuf, sendFlags);
        }

        return Error.Ok;
    }

    /// <inheritdoc/>
    public override byte[] _GetPacketScript()
    {
        if (_incoming.Count == 0)
            return System.Array.Empty<byte>();

        var pkt = _incoming.Dequeue();
        _currentPacket = pkt;

        // Note: Task 6 (docs/decisions/godot-get-packet-memory-contract.md) warns that Godot
        // holds a raw pointer after _GetPacketScript returns — that concern applies to the GDExtension
        // C++ virtual, not this C# _GetPacketScript() override, where Godot holds a managed GC
        // reference to the returned byte[]. We allocate exactly the right size per packet here.
        int payloadSize = pkt.Size;
        var result = new byte[payloadSize];
        if (payloadSize > 0)
            pkt.Buffer.AsSpan(0, payloadSize).CopyTo(result);

        // Return the rented ArrayPool buffer — result is now the independent copy Godot will hold
        System.Buffers.ArrayPool<byte>.Shared.Return(pkt.Buffer);

        return result;
    }

    /// <inheritdoc/>
    public override int _GetPacketPeer()
    {
        if (_peerMapper.TryGetGodotId(_currentPacket.Connection, out int id))
            return id;
        return 0;
    }

    /// <inheritdoc/>
    public override int _GetPacketChannel() => _currentPacket.Channel;

    /// <inheritdoc/>
    public override MultiplayerPeer.TransferModeEnum _GetPacketMode()
        => _currentPacket.Kind == PacketKind.Data ? _transferMode : MultiplayerPeer.TransferModeEnum.Reliable;

    /// <inheritdoc/>
    public override void _Close()
    {
        if (_state is PeerState.Idle or PeerState.Disconnected) return;

        _state = PeerState.Disconnecting;

        _core.Close();
        _peerMapper.Clear();
        _pendingHandshakes.Clear();

        // Drain and discard any queued incoming packets
        while (_incoming.Count > 0)
            System.Buffers.ArrayPool<byte>.Shared.Return(_incoming.Dequeue().Buffer);

        _state    = PeerState.Disconnected;
        _uniqueId = 0;
        _isServer = false;
    }

    /// <inheritdoc/>
    public override void _DisconnectPeer(int pPeer, bool pForce)
    {
        if (!_isServer) return; // only the server can disconnect individual peers

        if (!_peerMapper.TryGetConnection(pPeer, out uint conn)) return;

        _core.CloseConnection(conn, reason: 0, debugMsg: null, linger: !pForce);
        _peerMapper.Remove(pPeer);
        EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, (long)pPeer);
        EmitSignalPeerDisconnectedWithReason(pPeer, 0, "Disconnected by server", wasLocal: true);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>Computes the Steam send flags for the current transfer mode and options.</summary>
    private int ComputeSendFlags()
    {
        int flags = _transferMode switch
        {
            MultiplayerPeer.TransferModeEnum.Reliable          => k_nSteamNetworkingSend_Reliable,
            MultiplayerPeer.TransferModeEnum.UnreliableOrdered => k_nSteamNetworkingSend_Unreliable, // degrades (one-time warning fires in _SetTransferMode)
            _                                                  => k_nSteamNetworkingSend_UnreliableNoDelay,
        };
        if (NoNagle) flags |= k_nSteamNetworkingSend_NoNagle;
        if (NoDelay) flags |= k_nSteamNetworkingSend_NoDelay;
        return flags;
    }

    /// <summary>
    /// Returns the connection handle for the given Godot peer ID.
    /// For clients, peer ID 1 maps to the server connection.
    /// Returns 0 if the peer is not found.
    /// </summary>
    private uint GetConnectionForPeer(int godotId)
    {
        if (_isServer)
            return _peerMapper.TryGetConnection(godotId, out uint conn) ? conn : 0u;

        // Client: the only remote peer is the server (always ID 1)
        return godotId == 1 ? _core.ServerConnection : 0u;
    }

    /// <summary>Emits the extended peer-disconnected-with-reason signal and records disconnect info.</summary>
    private void EmitSignalPeerDisconnectedWithReason(int peerId, int code, string reason, bool wasLocal)
    {
        LastDisconnectInfo = new DisconnectInfo { Code = code, Reason = reason, WasLocalClose = wasLocal };
        // Signal name is the delegate name stripped of "EventHandler" suffix (Godot convention)
        EmitSignal("PeerDisconnectedWithReason", peerId, code, reason, wasLocal);
    }

    /// <summary>Handles a remote-initiated disconnect for the given connection handle.</summary>
    private void HandleRemoteDisconnect(uint connection)
    {
        if (_peerMapper.TryGetGodotId(connection, out int godotId))
        {
            _peerMapper.Remove(godotId);
            EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, (long)godotId);
            EmitSignalPeerDisconnectedWithReason(godotId, 0, "Remote disconnect", wasLocal: false);
        }
        else if (!_isServer)
        {
            // Client disconnected from server
            _state = PeerState.Disconnected;
            LastDisconnectInfo = new DisconnectInfo { Code = 0, Reason = "Server disconnected", WasLocalClose = false };
            EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, 1L);   // triggers Godot's server_disconnected
            EmitSignalPeerDisconnectedWithReason(1, 0, "Server disconnected", wasLocal: false);
        }
    }

    /// <summary>
    /// Checks for handshake timeouts and closes stalled connections.
    /// Any pending handshake that has exceeded the 5-second deadline is closed and cleaned up.
    /// </summary>
    private void CheckHandshakeTimeouts()
    {
        if (_pendingHandshakes.Count == 0) return;

        var expired = new System.Collections.Generic.List<uint>();
        foreach (var (conn, hs) in _pendingHandshakes)
            if (hs.IsExpired) expired.Add(conn);

        foreach (var conn in expired)
        {
            _pendingHandshakes.Remove(conn);
            if (_peerMapper.TryGetGodotId(conn, out int peerId))
                _peerMapper.Remove(peerId);
            _core.CloseConnection(conn, reason: 0, debugMsg: "Handshake timeout", linger: false);
        }
    }

    // ── Core event handlers ───────────────────────────────────────────────────

    /// <summary>Called by SteamNetworkingCore when a new incoming connection arrives (host only).</summary>
    private void OnIncomingConnection(uint connection)
    {
        if (!_isServer || _refusingNewConnections) return;

        // Get remote Steam ID from the connection info
        SteamId remoteSteamId = _core.GetRemoteSteamId(connection);
        if (!remoteSteamId.IsValid) return;  // can't map an invalid ID

        // Accept the connection and add to poll group
        _core.AcceptAndTrack(connection);

        // Assign Godot peer ID and register mappings
        int godotId;
        try
        {
            godotId = _peerMapper.Register(remoteSteamId, connection);
        }
        catch (InvalidOperationException)
        {
            // SteamId already registered — this peer is already connected
            _core.CloseConnection(connection, reason: 0, debugMsg: "Already connected", linger: false);
            return;
        }

        // Send handshake with assigned peer ID
        byte[] handshake = HandshakeProtocol.BuildHandshake(godotId);
        _core.SendTo(connection, handshake, sendFlags: k_nSteamNetworkingSend_Reliable);

        // Start 5-second handshake timeout
        _pendingHandshakes[connection] = new HandshakeState();
    }

    /// <summary>Called by SteamNetworkingCore when a connection's status changes.</summary>
    private void OnConnectionStatusChanged(uint connection, int newState, int oldState, string debugMsg)
    {
        if (!_isServer)
        {
            // Client: when server confirms connection (state → Connected), we're in Authenticating
            if (newState == k_ESteamNetworkingConnectionState_Connected)
            {
                _state = PeerState.Authenticating;
                // Handshake packet will come from server in _Poll
            }
            else if (newState == k_ESteamNetworkingConnectionState_ClosedByPeer
                  || newState == k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                HandleRemoteDisconnect(connection);
            }
        }
        else
        {
            // Host: handle peer disconnects from connection status changes
            if (newState == k_ESteamNetworkingConnectionState_ClosedByPeer
             || newState == k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                // Remove any pending handshake
                _pendingHandshakes.Remove(connection);
                HandleRemoteDisconnect(connection);
            }
        }
    }
}
