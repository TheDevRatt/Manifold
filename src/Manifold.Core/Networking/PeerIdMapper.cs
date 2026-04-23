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
    private int _nextId = 2; // Godot peer IDs start at 2; 1 is always the server

    /// <summary>
    /// Registers a new peer, assigning the next available Godot peer ID.
    /// Godot peer IDs start at 2 — 1 is always the server.
    /// </summary>
    /// <returns>The assigned Godot peer ID.</returns>
    internal int Register(SteamId steamId, uint connection)
    {
        int id = _nextId++;
        _steamToGodot[steamId]   = id;
        _godotToSteam[id]        = steamId;
        _connToGodot[connection] = id;
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
        // Remove all connection handles that map to this godot ID
        var toRemove = new List<uint>();
        foreach (var kv in _connToGodot)
            if (kv.Value == godotId) toRemove.Add(kv.Key);
        foreach (var k in toRemove) _connToGodot.Remove(k);
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

    /// <summary>
    /// Removes all mappings and resets the next peer ID to 2.
    /// Called when a <c>SteamMultiplayerPeer</c> closes.
    /// </summary>
    internal void Clear()
    {
        _steamToGodot.Clear();
        _godotToSteam.Clear();
        _connToGodot.Clear();
        _nextId = 2;
    }
}
