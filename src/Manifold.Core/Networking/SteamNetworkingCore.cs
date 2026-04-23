// Manifold — SteamNetworkingCore
// Internal wrapper around ISteamNetworkingSockets.
// Manages P2P connection lifecycle for both host and client roles.

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Manifold.Core.Dispatch;
using Manifold.Core.Interop;
using Manifold.Core.Testing;

namespace Manifold.Core.Networking;

/// <summary>
/// Internal wrapper around <c>ISteamNetworkingSockets</c>.
/// Manages P2P connection lifecycle for both host and client roles.
/// Not part of any public API — consumed internally by <c>SteamMultiplayerPeer</c>.
/// (MASTER_DESIGN §8.1)
/// </summary>
internal sealed class SteamNetworkingCore
{
    private readonly INetworkingBackend _backend;

    private bool _isHost;

    // Host-only
    private uint _listenSocket;  // HSteamListenSocket
    private uint _pollGroup;     // HSteamNetPollGroup

    // Client-only
    private uint _serverConnection;  // HSteamNetConnection to the server

    // Callback subscription
    private IDisposable? _statusSubscription;

    /// <summary>Maximum number of messages to drain per poll frame.</summary>
    internal const int MaxMessagesPerFrame = 512;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when an incoming connection arrives (host only).
    /// The connection handle must be accepted via <see cref="AcceptAndTrack"/> to proceed.
    /// Parameters: (HSteamNetConnection connection)
    /// </summary>
    internal event Action<uint>? IncomingConnection;

    /// <summary>
    /// Raised when a connection's status changes (connect, disconnect, etc.).
    /// Parameters: (HSteamNetConnection connection, int newState, int oldState, string debugMessage)
    /// </summary>
    internal event Action<uint, int, int, string>? ConnectionStatusChanged;

    /// <summary>
    /// Creates a <see cref="SteamNetworkingCore"/> backed by the given networking backend.
    /// Use <see cref="FakeSteamBackend"/> in tests; use the live backend in production.
    /// </summary>
    internal SteamNetworkingCore(INetworkingBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        // Subscribe to connection status changes for the lifetime of this core.
        _statusSubscription = CallbackDispatcher.Subscribe<SteamNetConnectionStatusChangedCallback_t>(
            OnConnectionStatusChangedCallback);
    }

    // ── Host path ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a P2P listen socket and poll group for hosting.
    /// </summary>
    /// <param name="virtualPort">The virtual port to listen on. Must match the port clients connect to.</param>
    /// <returns>The listen socket handle; <see cref="ListenSocket.Invalid"/> on failure.</returns>
    internal ListenSocket CreateHost(int virtualPort = 0)
    {
        _isHost       = true;
        _listenSocket = _backend.CreateListenSocketP2P(virtualPort);
        _pollGroup    = _backend.CreatePollGroup();
        return new ListenSocket(_listenSocket);
    }

    // ── Client path ────────────────────────────────────────────────────────────

    /// <summary>
    /// Initiates a P2P connection to the host identified by their Steam64 ID.
    /// </summary>
    /// <param name="hostSteamId">The host's Steam64 ID.</param>
    /// <param name="virtualPort">Virtual port to connect on. Must match the host's listen socket port.</param>
    /// <returns>The connection handle; <see cref="NetConnection.Invalid"/> on failure.</returns>
    internal NetConnection CreateClient(SteamId hostSteamId, int virtualPort = 0)
    {
        _isHost = false;
        _serverConnection = _backend.ConnectP2P((ulong)hostSteamId, virtualPort);
        return new NetConnection(_serverConnection);
    }

    // ── Connection management (host only) ─────────────────────────────────────

    /// <summary>
    /// Accepts an incoming connection and adds it to the poll group.
    /// Must only be called on the host.
    /// </summary>
    internal void AcceptAndTrack(uint connection)
    {
        System.Diagnostics.Debug.Assert(_isHost,
            "AcceptAndTrack must only be called in host mode. _isHost is false.");
        _backend.AcceptConnection(connection);
        _backend.SetConnectionPollGroup(connection, _pollGroup);
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a packet on the specified connection.
    /// </summary>
    /// <param name="connection">The target connection handle.</param>
    /// <param name="data">Packet data (including the 2-byte Manifold header).</param>
    /// <param name="sendFlags">Steam send flags (reliable, unreliable, etc.).</param>
    /// <returns>An <c>EResult</c> value (1 = OK).</returns>
    internal int SendTo(uint connection, ReadOnlySpan<byte> data, int sendFlags)
        => _backend.SendMessageToConnection(connection, data, sendFlags);

    // ── Connection status callback ────────────────────────────────────────────

    /// <summary>
    /// Raw callback handler wired to <see cref="CallbackDispatcher"/>.
    /// Unpacks the Steam struct and delegates to <see cref="HandleConnectionStatusChanged"/>.
    /// </summary>
    private unsafe void OnConnectionStatusChangedCallback(SteamNetConnectionStatusChangedCallback_t cb)
    {
        // Extract the debug string from the fixed-byte buffer.
        string debugMsg = Marshal.PtrToStringAnsi((IntPtr)cb.m_info.m_szEndDebug) ?? string.Empty;
        HandleConnectionStatusChanged(cb.m_hConn, cb.m_info.m_eState, cb.m_eOldState, debugMsg);
    }

    /// <summary>
    /// Processes a connection status change. Called by the callback subscriber and also directly by tests.
    /// </summary>
    internal void HandleConnectionStatusChanged(uint connection, int newState, int oldState, string debugMsg)
    {
        const int k_ESteamNetworkingConnectionState_Connecting           = 1;
        // const int k_ESteamNetworkingConnectionState_Connected         = 3;
        // const int k_ESteamNetworkingConnectionState_ClosedByPeer      = 4;
        // const int k_ESteamNetworkingConnectionState_ProblemDetectedLocally = 6;

        if (_isHost && newState == k_ESteamNetworkingConnectionState_Connecting)
        {
            // Incoming connection — fire event; SteamMultiplayerPeer will call AcceptAndTrack
            IncomingConnection?.Invoke(connection);
        }
        else
        {
            ConnectionStatusChanged?.Invoke(connection, newState, oldState, debugMsg);
        }
    }

    // ── Close ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Closes a specific connection.
    /// </summary>
    internal void CloseConnection(uint connection, int reason = 0, string? debugMsg = null, bool linger = false)
        => _backend.CloseConnection(connection, reason, debugMsg, linger);

    /// <summary>
    /// Closes all connections and releases host resources (listen socket + poll group).
    /// Safe to call in either host or client mode. Idempotent — safe to call multiple times.
    /// </summary>
    internal void Close()
    {
        _statusSubscription?.Dispose();
        _statusSubscription = null;

        if (_isHost)
        {
            _backend.CloseListenSocket(_listenSocket);
            _backend.DestroyPollGroup(_pollGroup);
            _listenSocket = 0;
            _pollGroup    = 0;
            _isHost       = false;  // reset so second call is no-op
        }
        else if (_serverConnection != 0)
        {
            _backend.CloseConnection(_serverConnection, 0, null, false);
            _serverConnection = 0;  // reset so second call is no-op
        }
    }

    // ── Server connection accessor (client only) ──────────────────────────────

    /// <summary>The server connection handle. Only valid in client mode after <see cref="CreateClient"/>.</summary>
    internal uint ServerConnection => _serverConnection;

    // ── Receive ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Drains all pending Steam messages into <paramref name="output"/>.
    /// Host uses the poll group (one native call regardless of peer count).
    /// Client uses per-connection receive.
    /// Each <see cref="ReceivedPacket.Buffer"/> is rented from <see cref="ArrayPool{T}.Shared"/>;
    /// the consumer is responsible for returning it when done.
    /// (MASTER_DESIGN §8.4.2)
    /// </summary>
    internal unsafe void DrainMessages(System.Collections.Generic.List<ReceivedPacket> output)
    {
        var msgPtrs = ArrayPool<IntPtr>.Shared.Rent(MaxMessagesPerFrame);
        try
        {
            int count;
            if (_isHost)
                count = _backend.ReceiveMessagesOnPollGroup(_pollGroup, msgPtrs, MaxMessagesPerFrame);
            else
                count = _backend.ReceiveMessagesOnConnection(_serverConnection, msgPtrs, MaxMessagesPerFrame);

            for (int i = 0; i < count; i++)
                ProcessMessage(msgPtrs[i], output);
        }
        finally
        {
            ArrayPool<IntPtr>.Shared.Return(msgPtrs);
        }
    }

    private static unsafe void ProcessMessage(
        IntPtr msgPtr,
        System.Collections.Generic.List<ReceivedPacket> output)
    {
        ref var msg = ref *(SteamNetworkingMessage_t*)msgPtr;
        try
        {
            if (msg.m_cbSize < PacketHeader.Size) return; // too short to have a header

            var rawData = new System.ReadOnlySpan<byte>((void*)msg.m_pData, msg.m_cbSize);
            if (!PacketHeader.TryDecode(rawData, out var header)) return;

            int payloadSize = msg.m_cbSize - PacketHeader.Size;
            // Rent a buffer for the payload. Consumer returns it to the pool when done.
            // Note: we rent even for 0-byte payloads (control packets) for uniformity.
            int rentSize = payloadSize > 0 ? payloadSize : 1;
            var rentedBuf = ArrayPool<byte>.Shared.Rent(rentSize);

            if (payloadSize > 0)
                rawData.Slice(PacketHeader.Size, payloadSize).CopyTo(rentedBuf);

            output.Add(new ReceivedPacket
            {
                Buffer     = rentedBuf,
                Size       = payloadSize,
                Connection = msg.m_conn,
                Channel    = header.Channel,
                Kind       = header.Kind,
            });
        }
        finally
        {
            msg.Release(); // always release the Steam-owned message
        }
    }
}
