// Manifold — SteamNetworkingCore
// Internal wrapper around ISteamNetworkingSockets.
// Manages P2P connection lifecycle for both host and client roles.

using System;
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

    /// <summary>Maximum number of messages to drain per poll frame.</summary>
    internal const int MaxMessagesPerFrame = 512;

    /// <summary>
    /// Creates a <see cref="SteamNetworkingCore"/> backed by the given networking backend.
    /// Use <see cref="FakeSteamBackend"/> in tests; use the live backend in production.
    /// </summary>
    internal SteamNetworkingCore(INetworkingBackend backend)
        => _backend = backend ?? throw new ArgumentNullException(nameof(backend));

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

    // ── Close ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Closes a specific connection.
    /// </summary>
    internal void CloseConnection(uint connection, int reason = 0, string? debugMsg = null, bool linger = false)
        => _backend.CloseConnection(connection, reason, debugMsg, linger);

    /// <summary>
    /// Closes all connections and releases host resources (listen socket + poll group).
    /// Safe to call in either host or client mode.
    /// </summary>
    internal void Close()
    {
        if (_isHost)
        {
            _backend.CloseListenSocket(_listenSocket);
            _backend.DestroyPollGroup(_pollGroup);
        }
        else if (_serverConnection != 0)
        {
            _backend.CloseConnection(_serverConnection, 0, null, false);
        }
    }

    // ── Server connection accessor (client only) ──────────────────────────────

    /// <summary>The server connection handle. Only valid in client mode after <see cref="CreateClient"/>.</summary>
    internal uint ServerConnection => _serverConnection;
}
