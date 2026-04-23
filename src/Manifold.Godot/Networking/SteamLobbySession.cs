using Godot;
using Manifold.Core;

namespace Manifold.Godot.Networking;

/// <summary>
/// Manages the lifecycle of a single Steam lobby session.
/// Created by <see cref="SteamMultiplayerPeer"/> when hosting or joining via
/// <c>HostWithLobbyAsync</c> or <c>JoinLobbyAsync</c>.
/// (MASTER_DESIGN §3)
/// </summary>
/// <remarks>
/// <para><b>Phase 3 stub</b>: Full implementation requires <c>SteamMatchmaking</c>
/// (Phase 3 API surface). All members return safe defaults.</para>
/// </remarks>
public partial class SteamLobbySession : RefCounted
{
    /// <summary>The Steam64 ID of this lobby. <see cref="SteamId.Invalid"/> if not active.</summary>
    public SteamId LobbyId { get; internal set; } = SteamId.Invalid;

    /// <summary>The Steam64 ID of the lobby owner.</summary>
    public SteamId OwnerSteamId { get; internal set; } = SteamId.Invalid;

    /// <summary>Current number of members in the lobby.</summary>
    public int MemberCount { get; internal set; }

    /// <summary><c>true</c> if this session represents a valid active lobby.</summary>
    public bool IsValid => LobbyId.IsValid;

    // ── Phase 3 stubs — full implementation when SteamMatchmaking is complete ──

    /// <summary>
    /// Returns all current lobby member Steam IDs.
    /// <para><b>Phase 3 stub</b>: always returns an empty array.</para>
    /// </summary>
    public SteamId[] GetMembers() => System.Array.Empty<SteamId>();

    /// <summary>
    /// Gets a lobby metadata value by key.
    /// <para><b>Phase 3 stub</b>: always returns empty string.</para>
    /// </summary>
    public string GetData(string key) => string.Empty;

    /// <summary>
    /// Sets a lobby metadata key-value pair.
    /// <para><b>Phase 3 stub</b>: always returns <c>false</c>.</para>
    /// </summary>
    public bool SetData(string key, string value) => false;

    /// <summary>A shared invalid session instance for use as a null-object default.</summary>
    public static SteamLobbySession Invalid { get; } = new() { LobbyId = SteamId.Invalid };
}
