// In-memory test double for all Steam backend capability interfaces.

using System;

namespace Manifold.Core.Testing;

/// <summary>
/// A configurable test double for all Steam backend capability interfaces.
/// Provides safe defaults for every method; override properties to control
/// return values in specific tests.
/// Records all call names in <see cref="CallLog"/> for assertion.
/// </summary>
public class FakeSteamBackend : IUserBackend, IMatchmakingBackend, INetworkingBackend
{
    /// <summary>Log of all method names called on this instance, in order.</summary>
    public List<string> CallLog { get; } = new();

    private void Record(string name) => CallLog.Add(name);

    /// <summary>Value returned by <see cref="BLoggedOn"/>. Default: <c>true</c>.</summary>
    public bool IsLoggedOn { get; set; } = true;

    /// <summary>Value returned by <see cref="GetSteamID"/>. Default: a recognisable fake ID.</summary>
    public ulong LocalSteamId { get; set; } = 76561198000000001UL;

    /// <summary>Next <c>SteamAPICall_t</c> handle to return from async operations.</summary>
    public ulong NextApiCallHandle { get; set; } = 1000;

    /// <summary>Value returned by <see cref="GetLobbyData"/>.</summary>
    public string LobbyData { get; set; } = string.Empty;

    /// <summary>Value returned by <see cref="GetNumLobbyMembers"/>.</summary>
    public int LobbyMemberCount { get; set; } = 0;

    /// <summary>Value returned by <see cref="GetLobbyOwner"/>.</summary>
    public ulong LobbyOwner { get; set; } = 0;

    /// <inheritdoc/>
    public ulong GetSteamID()    { Record(nameof(GetSteamID));  return LocalSteamId; }

    /// <inheritdoc/>
    public bool BLoggedOn()      { Record(nameof(BLoggedOn));   return IsLoggedOn; }

    /// <inheritdoc/>
    public ulong CreateLobby(int lobbyType, int maxMembers)
    {
        Record(nameof(CreateLobby));
        return NextApiCallHandle++;
    }

    /// <inheritdoc/>
    public ulong JoinLobby(ulong steamIdLobby)
    {
        Record(nameof(JoinLobby));
        return NextApiCallHandle++;
    }

    /// <inheritdoc/>
    public void LeaveLobby(ulong steamIdLobby)          => Record(nameof(LeaveLobby));

    /// <inheritdoc/>
    public int GetNumLobbyMembers(ulong steamIdLobby)
    {
        Record(nameof(GetNumLobbyMembers));
        return LobbyMemberCount;
    }

    /// <inheritdoc/>
    public ulong GetLobbyMemberByIndex(ulong steamIdLobby, int member)
    {
        Record(nameof(GetLobbyMemberByIndex));
        return 0;
    }

    /// <inheritdoc/>
    public ulong GetLobbyOwner(ulong steamIdLobby)
    {
        Record(nameof(GetLobbyOwner));
        return LobbyOwner;
    }

    /// <inheritdoc/>
    public string GetLobbyData(ulong steamIdLobby, string key)
    {
        Record(nameof(GetLobbyData));
        return LobbyData;
    }

    /// <inheritdoc/>
    public bool SetLobbyData(ulong steamIdLobby, string key, string value)
    {
        Record(nameof(SetLobbyData));
        return true;
    }

    /// <inheritdoc/>
    public bool SetLobbyMemberLimit(ulong steamIdLobby, int maxMembers)
    {
        Record(nameof(SetLobbyMemberLimit));
        return true;
    }

    /// <inheritdoc/>
    public bool SetLobbyJoinable(ulong steamIdLobby, bool joinable)
    {
        Record(nameof(SetLobbyJoinable));
        return true;
    }

    /// <inheritdoc/>
    public uint CreateListenSocketP2P(int virtualPort)
    {
        Record(nameof(CreateListenSocketP2P));
        return 1;
    }

    /// <inheritdoc/>
    public uint ConnectP2P(ulong remoteIdentitySteamId, int virtualPort)
    {
        Record(nameof(ConnectP2P));
        return 2;
    }

    /// <inheritdoc/>
    public uint CreatePollGroup()
    {
        Record(nameof(CreatePollGroup));
        return 1;
    }

    /// <inheritdoc/>
    public bool DestroyPollGroup(uint pollGroup)
    {
        Record(nameof(DestroyPollGroup));
        return true;
    }

    /// <inheritdoc/>
    public bool SetConnectionPollGroup(uint conn, uint pollGroup)
    {
        Record(nameof(SetConnectionPollGroup));
        return true;
    }

    /// <inheritdoc/>
    public int AcceptConnection(uint conn)
    {
        Record(nameof(AcceptConnection));
        return 1; // EResult.OK
    }

    /// <inheritdoc/>
    public bool CloseConnection(uint conn, int reason, string? debug, bool enableLinger)
    {
        Record(nameof(CloseConnection));
        return true;
    }

    /// <inheritdoc/>
    public bool CloseListenSocket(uint socket)
    {
        Record(nameof(CloseListenSocket));
        return true;
    }

    /// <inheritdoc/>
    public int SendMessageToConnection(uint hConn, ReadOnlySpan<byte> data, int sendFlags)
    {
        Record(nameof(SendMessageToConnection));
        return 1; // EResult.OK
    }

    /// <inheritdoc/>
    public int ReceiveMessagesOnPollGroup(uint pollGroup, IntPtr[] ppOut, int maxMessages)
    {
        Record(nameof(ReceiveMessagesOnPollGroup));
        return 0;
    }

    /// <inheritdoc/>
    public int ReceiveMessagesOnConnection(uint hConn, IntPtr[] ppOut, int maxMessages)
    {
        Record(nameof(ReceiveMessagesOnConnection));
        return 0;
    }
}
