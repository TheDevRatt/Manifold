// Backend capability interfaces — one per Steam API subsystem.

namespace Manifold.Core.Testing;

/// <summary>
/// Capability interface covering <c>ISteamUser</c> operations.
/// Implement in <see cref="FakeSteamBackend"/> to control user state in tests.
/// </summary>
public interface IUserBackend
{
    /// <summary>Returns the local user's Steam ID as a raw ulong.</summary>
    ulong GetSteamID();

    /// <summary>Returns <c>true</c> if the user is currently logged into Steam.</summary>
    bool BLoggedOn();
}

/// <summary>
/// Capability interface covering <c>ISteamMatchmaking</c> lobby operations.
/// </summary>
public interface IMatchmakingBackend
{
    /// <summary>Initiates an async lobby creation. Returns the <c>SteamAPICall_t</c> handle.</summary>
    ulong CreateLobby(int lobbyType, int maxMembers);

    /// <summary>Initiates an async lobby join. Returns the <c>SteamAPICall_t</c> handle.</summary>
    ulong JoinLobby(ulong steamIdLobby);

    /// <summary>Leaves the specified lobby immediately.</summary>
    void LeaveLobby(ulong steamIdLobby);

    /// <summary>Returns the number of members currently in the lobby.</summary>
    int GetNumLobbyMembers(ulong steamIdLobby);

    /// <summary>Returns the Steam ID of the lobby member at the given index.</summary>
    ulong GetLobbyMemberByIndex(ulong steamIdLobby, int member);

    /// <summary>Returns the Steam ID of the lobby owner.</summary>
    ulong GetLobbyOwner(ulong steamIdLobby);

    /// <summary>Returns a lobby metadata value by key.</summary>
    string GetLobbyData(ulong steamIdLobby, string key);

    /// <summary>Sets a lobby metadata value. Returns <c>true</c> on success.</summary>
    bool SetLobbyData(ulong steamIdLobby, string key, string value);

    /// <summary>Sets the maximum number of lobby members. Returns <c>true</c> on success.</summary>
    bool SetLobbyMemberLimit(ulong steamIdLobby, int maxMembers);

    /// <summary>Sets the lobby joinable state. Returns <c>true</c> on success.</summary>
    bool SetLobbyJoinable(ulong steamIdLobby, bool joinable);
}

/// <summary>
/// Capability interface covering <c>ISteamNetworkingSockets</c> operations.
/// </summary>
public interface INetworkingBackend
{
    /// <summary>Creates a P2P listen socket on the given virtual port.</summary>
    uint CreateListenSocketP2P(int virtualPort);

    /// <summary>Connects to a remote host identified by their Steam ID.</summary>
    uint ConnectP2P(ulong remoteIdentitySteamId, int virtualPort);

    /// <summary>Creates a poll group for efficient multi-connection receive.</summary>
    uint CreatePollGroup();

    /// <summary>Destroys a poll group previously created with <see cref="CreatePollGroup"/>.</summary>
    bool DestroyPollGroup(uint pollGroup);

    /// <summary>Assigns a connection to a poll group.</summary>
    bool SetConnectionPollGroup(uint conn, uint pollGroup);

    /// <summary>Accepts an incoming connection on a listen socket.</summary>
    int AcceptConnection(uint conn);

    /// <summary>Closes a connection with the given reason code and debug string.</summary>
    bool CloseConnection(uint conn, int reason, string? debug, bool enableLinger);

    /// <summary>Closes a listen socket.</summary>
    bool CloseListenSocket(uint socket);
}
