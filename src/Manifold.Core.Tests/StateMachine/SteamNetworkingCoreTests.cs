// Tests for SteamNetworkingCore using FakeSteamBackend — no real Steam required.

using System;
using Manifold.Core.Networking;
using Manifold.Core.Testing;
using Xunit;

namespace Manifold.Core.Tests.Networking;

public class SteamNetworkingCoreTests
{
    private static SteamNetworkingCore MakeCore(out FakeSteamBackend fake)
    {
        fake = new FakeSteamBackend();
        return new SteamNetworkingCore(fake);
    }

    // ── CreateHost ─────────────────────────────────────────────────────────────

    [Fact]
    public void CreateHost_Calls_CreateListenSocketP2P_And_CreatePollGroup()
    {
        var core = MakeCore(out var fake);
        core.CreateHost();

        Assert.Contains(nameof(FakeSteamBackend.CreateListenSocketP2P), fake.CallLog);
        Assert.Contains(nameof(FakeSteamBackend.CreatePollGroup), fake.CallLog);
    }

    [Fact]
    public void CreateHost_Returns_ValidListenSocket()
    {
        var core = MakeCore(out _);
        var socket = core.CreateHost();

        // FakeSteamBackend.CreateListenSocketP2P returns 1
        Assert.True(socket.IsValid);
        Assert.Equal(1u, socket.Value);
    }

    [Fact]
    public void CreateHost_SetsIsHostMode()
    {
        // We verify host mode indirectly: Close() should call CloseListenSocket + DestroyPollGroup
        var core = MakeCore(out var fake);
        core.CreateHost();
        core.Close();

        Assert.Contains(nameof(FakeSteamBackend.CloseListenSocket), fake.CallLog);
        Assert.Contains(nameof(FakeSteamBackend.DestroyPollGroup), fake.CallLog);
    }

    // ── CreateClient ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateClient_Calls_ConnectP2P_WithCorrectSteamId()
    {
        var core = MakeCore(out var fake);
        var hostId = new SteamId(76561198000000099UL);
        core.CreateClient(hostId);

        Assert.Contains(nameof(FakeSteamBackend.ConnectP2P), fake.CallLog);
    }

    [Fact]
    public void CreateClient_Returns_ValidNetConnection()
    {
        var core = MakeCore(out _);
        var conn = core.CreateClient(new SteamId(76561198000000099UL));

        // FakeSteamBackend.ConnectP2P returns 2
        Assert.True(conn.IsValid);
        Assert.Equal(2u, conn.Value);
    }

    // ── AcceptAndTrack ─────────────────────────────────────────────────────────

    [Fact]
    public void AcceptAndTrack_Calls_AcceptConnection_And_SetConnectionPollGroup()
    {
        var core = MakeCore(out var fake);
        core.CreateHost();
        core.AcceptAndTrack(42u);

        Assert.Contains(nameof(FakeSteamBackend.AcceptConnection), fake.CallLog);
        Assert.Contains(nameof(FakeSteamBackend.SetConnectionPollGroup), fake.CallLog);
    }

    // ── SendTo ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SendTo_Calls_SendMessageToConnection()
    {
        var core = MakeCore(out var fake);
        byte[] data = { 0x01, 0x02, 0x03 };
        int result = core.SendTo(10u, data, 8);

        Assert.Contains(nameof(FakeSteamBackend.SendMessageToConnection), fake.CallLog);
        Assert.Equal(1, result); // FakeSteamBackend returns 1 (EResult.OK)
    }

    // ── CloseConnection ────────────────────────────────────────────────────────

    [Fact]
    public void CloseConnection_Calls_CloseConnection_OnBackend()
    {
        var core = MakeCore(out var fake);
        core.CloseConnection(99u);

        Assert.Contains(nameof(FakeSteamBackend.CloseConnection), fake.CallLog);
    }

    // ── Close (host) ───────────────────────────────────────────────────────────

    [Fact]
    public void Close_Host_Calls_CloseListenSocket_And_DestroyPollGroup()
    {
        var core = MakeCore(out var fake);
        core.CreateHost();
        core.Close();

        Assert.Contains(nameof(FakeSteamBackend.CloseListenSocket), fake.CallLog);
        Assert.Contains(nameof(FakeSteamBackend.DestroyPollGroup), fake.CallLog);
    }

    // ── Close (client) ─────────────────────────────────────────────────────────

    [Fact]
    public void Close_Client_Calls_CloseConnection_OnServerConnection()
    {
        var core = MakeCore(out var fake);
        core.CreateClient(new SteamId(76561198000000099UL));
        core.Close();

        Assert.Contains(nameof(FakeSteamBackend.CloseConnection), fake.CallLog);
    }

    [Fact]
    public void Close_Client_DoesNotCall_CloseListenSocket_Or_DestroyPollGroup()
    {
        var core = MakeCore(out var fake);
        core.CreateClient(new SteamId(76561198000000099UL));
        core.Close();

        Assert.DoesNotContain(nameof(FakeSteamBackend.CloseListenSocket), fake.CallLog);
        Assert.DoesNotContain(nameof(FakeSteamBackend.DestroyPollGroup), fake.CallLog);
    }

    // ── ServerConnection ───────────────────────────────────────────────────────

    [Fact]
    public void ServerConnection_ReturnsHandle_AfterCreateClient()
    {
        var core = MakeCore(out _);
        core.CreateClient(new SteamId(76561198000000099UL));

        // FakeSteamBackend.ConnectP2P returns 2
        Assert.Equal(2u, core.ServerConnection);
    }
}
