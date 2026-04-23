using System;
using System.Collections.Generic;

namespace Manifold.Core.Networking;

/// <summary>
/// Bidirectional mapping between Steam peer identities and Godot multiplayer peer IDs.
/// Maps: SteamId ↔ Godot peer ID ↔ HSteamNetConnection handle.
/// Not thread-safe — must be accessed only from the game thread.
/// (MASTER_DESIGN §8.9)
/// </summary>
internal sealed class PeerIdMapper
{
    private readonly Dictionary<SteamId, int> _steamToGodot = new();
    private readonly Dictionary<int, SteamId> _godotToSteam = new();
    private readonly Dictionary<uint, int>    _connToGodot  = new(); // HSteamNetConnection → Godot ID
    /// <summary>Reverse of _connToGodot — maps Godot peer ID to its HSteamNetConnection handle. Enables O(1) removal.</summary>
    private readonly Dictionary<int, uint>    _godotToConn  = new();
    private int _nextId = 2; // Godot peer IDs start at 2; 1 is always the server

    /// <summary>
    /// Registers a new peer, assigning the next available Godot peer ID.
    /// Godot peer IDs start at 2 — 1 is always the server.
    /// </summary>
    /// <exception cref="InvalidOperationException">If <paramref name="steamId"/> is already registered.</exception>
    /// <returns>The assigned Godot peer ID.</returns>
    internal int Register(SteamId steamId, uint connection)
    {
        if (_steamToGodot.ContainsKey(steamId))
            throw new InvalidOperationException(
                $"SteamId {steamId} is already registered as Godot peer {_steamToGodot[steamId]}. " +
                "Call Remove() before re-registering.");

        // Godot peer IDs must be >= 2 (1 is the server). Wrap if we overflow.
        // In practice this should never happen in a game session, but guards against subtle corruption.
        if (_nextId < 2) _nextId = 2;

        int id = _nextId++;
        _steamToGodot[steamId]   = id;
        _godotToSteam[id]        = steamId;
        _connToGodot[connection] = id;
        _godotToConn[id]         = connection;
        return id;
    }

    /// <summary>
    /// Removes all mappings associated with the given Godot peer ID.
    /// No-op if the ID is not registered.
    /// </summary>
    internal void Remove(int godotId)
    {
        if (!_godotToSteam.TryGetValue(godotId, out var steamId)) return;
        _steamToGodot.Remove(steamId);
        _godotToSteam.Remove(godotId);
        // O(1) connection lookup via reverse map — no loop, no allocation
        if (_godotToConn.TryGetValue(godotId, out var conn))
        {
            _connToGodot.Remove(conn);
            _godotToConn.Remove(godotId);
        }
    }

    /// <summary>Returns the Steam ID for the given Godot peer ID.</summary>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If the Godot ID is not registered.</exception>
    internal SteamId GetSteamId(int godotId) => _godotToSteam[godotId];

    /// <summary>Returns the Godot peer ID for the given Steam ID.</summary>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If the Steam ID is not registered.</exception>
    internal int GetGodotId(SteamId steamId) => _steamToGodot[steamId];

    /// <summary>Returns the Godot peer ID for the given connection handle.</summary>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If the connection is not registered.</exception>
    internal int GetGodotId(uint connection) => _connToGodot[connection];

    /// <summary>
    /// Attempts to get the Godot peer ID for the given connection handle.
    /// Returns <c>false</c> if not found.
    /// </summary>
    internal bool TryGetGodotId(uint connection, out int godotId)
        => _connToGodot.TryGetValue(connection, out godotId);

    /// <summary>Returns the connection handle for the given Godot peer ID.</summary>
    /// <exception cref="KeyNotFoundException">If the Godot ID is not registered.</exception>
    internal uint GetConnection(int godotId) => _godotToConn[godotId];

    /// <summary>Attempts to get the connection handle for the given Godot peer ID.</summary>
    /// <returns><c>true</c> if found; <c>false</c> otherwise.</returns>
    internal bool TryGetConnection(int godotId, out uint connection)
        => _godotToConn.TryGetValue(godotId, out connection);

    /// <summary>
    /// Registers a peer with an explicit Godot peer ID (used to register the server as peer 1 on the client).
    /// Throws if the SteamId is already registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">If <paramref name="steamId"/> is already registered.</exception>
    internal void RegisterWithId(SteamId steamId, uint connection, int godotId)
    {
        if (_steamToGodot.ContainsKey(steamId))
            throw new InvalidOperationException($"SteamId {steamId} is already registered.");
        _steamToGodot[steamId]   = godotId;
        _godotToSteam[godotId]   = steamId;
        _connToGodot[connection] = godotId;
        _godotToConn[godotId]    = connection;
        // Don't increment _nextId — explicit ID registration bypasses auto-assignment
    }

    /// <summary>Enumerates all registered (connection, godotId) pairs.</summary>
    internal IEnumerable<(uint Connection, int GodotId)> GetAllConnections()
    {
        foreach (var kv in _connToGodot)
            yield return (kv.Key, kv.Value);
    }

    /// <summary>
    /// Removes all mappings and resets the next peer ID to 2.
    /// Called when a <c>SteamMultiplayerPeer</c> closes.
    /// </summary>
    internal void Clear()
    {
        _steamToGodot.Clear();
        _godotToSteam.Clear();
        _connToGodot.Clear();
        _godotToConn.Clear();
        _nextId = 2;
    }
}
