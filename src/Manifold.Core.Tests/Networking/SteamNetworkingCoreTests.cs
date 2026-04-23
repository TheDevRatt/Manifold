// Tests for SteamNetworkingCore using FakeSteamBackend — no real Steam required.

using System;
using System.Collections.Generic;
using System.Linq;
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
        Assert.Equal(76561198000000099UL, fake.LastConnectP2PSteamId);
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

    // ── Close (idempotent / cold-start) ────────────────────────────────────────

    [Fact]
    public void Close_BeforeAnySetup_DoesNotCallBackend()
    {
        var core = MakeCore(out var fake);
        core.Close(); // should not throw or call any backend method
        Assert.DoesNotContain("CloseListenSocket", fake.CallLog);
        Assert.DoesNotContain("CloseConnection",   fake.CallLog);
        Assert.DoesNotContain("DestroyPollGroup",  fake.CallLog);
    }

    [Fact]
    public void Close_Host_CalledTwice_IsIdempotent()
    {
        var core = MakeCore(out var fake);
        core.CreateHost();
        core.Close();
        core.Close(); // second call — should be a no-op
        // CloseListenSocket and DestroyPollGroup should appear exactly once each
        Assert.Equal(1, fake.CallLog.Count(x => x == "CloseListenSocket"));
        Assert.Equal(1, fake.CallLog.Count(x => x == "DestroyPollGroup"));
    }

    [Fact]
    public void Close_Client_CalledTwice_IsIdempotent()
    {
        var core = MakeCore(out var fake);
        core.CreateClient(new SteamId(1));
        core.Close();
        core.Close(); // second call — should be a no-op
        Assert.Equal(1, fake.CallLog.Count(x => x == "CloseConnection"));
    }

    // ── HandleConnectionStatusChanged ─────────────────────────────────────────

    [Fact]
    public void HandleConnectionStatusChanged_IncomingOnHost_FiresIncomingConnectionEvent()
    {
        var core = MakeCore(out _);
        core.CreateHost();
        uint? captured = null;
        core.IncomingConnection += conn => captured = conn;

        // State 1 = k_ESteamNetworkingConnectionState_Connecting
        core.HandleConnectionStatusChanged(connection: 99, newState: 1, oldState: 0, debugMsg: "");

        Assert.Equal(99u, captured);
    }

    [Fact]
    public void HandleConnectionStatusChanged_ConnectedOnHost_FiresConnectionStatusChangedNotIncoming()
    {
        var core = MakeCore(out _);
        core.CreateHost();
        uint? incomingConn = null;
        (uint conn, int newState)? statusChange = null;
        core.IncomingConnection += c => incomingConn = c;
        core.ConnectionStatusChanged += (c, ns, os, dbg) => statusChange = (c, ns);

        // State 3 = k_ESteamNetworkingConnectionState_Connected
        core.HandleConnectionStatusChanged(connection: 99, newState: 3, oldState: 1, debugMsg: "");

        Assert.Null(incomingConn);
        Assert.NotNull(statusChange);
        Assert.Equal(99u, statusChange!.Value.conn);
        Assert.Equal(3, statusChange!.Value.newState);
    }

    [Fact]
    public void HandleConnectionStatusChanged_IncomingOnClient_DoesNotFireIncomingConnectionEvent()
    {
        // Client mode: incoming connections don't make sense, should be ignored
        var core = MakeCore(out _);
        core.CreateClient(new SteamId(1));
        uint? captured = null;
        core.IncomingConnection += conn => captured = conn;

        core.HandleConnectionStatusChanged(connection: 99, newState: 1, oldState: 0, debugMsg: "");

        // Client is not a host — IncomingConnection should NOT fire
        Assert.Null(captured);
    }

    [Fact]
    public void Close_DisposesStatusSubscription()
    {
        // After Close(), the subscription token should be disposed (IDisposable.Dispose called)
        // We can't easily test CallbackDispatcher internals here, but we can verify Close()
        // doesn't throw when called after the subscription was set up.
        var core = MakeCore(out _);
        core.CreateHost();
        core.Close(); // should not throw
    }

    // ── DrainMessages ──────────────────────────────────────────────────────────

    [Fact]
    public void DrainMessages_Host_CallsReceiveMessagesOnPollGroup()
    {
        var core = MakeCore(out var fake);
        core.CreateHost();
        var list = new List<ReceivedPacket>();
        core.DrainMessages(list);
        Assert.Contains("ReceiveMessagesOnPollGroup", fake.CallLog);
        Assert.DoesNotContain("ReceiveMessagesOnConnection", fake.CallLog);
        Assert.Empty(list); // fake returns 0 messages
    }

    [Fact]
    public void DrainMessages_Client_CallsReceiveMessagesOnConnection()
    {
        var core = MakeCore(out var fake);
        core.CreateClient(new SteamId(1));
        var list = new List<ReceivedPacket>();
        core.DrainMessages(list);
        Assert.Contains("ReceiveMessagesOnConnection", fake.CallLog);
        Assert.DoesNotContain("ReceiveMessagesOnPollGroup", fake.CallLog);
        Assert.Empty(list);
    }

    [Fact]
    public void DrainMessages_EmptyResult_DoesNotAddToOutput()
    {
        var core = MakeCore(out var fake);
        core.CreateHost();
        var list = new List<ReceivedPacket>();
        core.DrainMessages(list);
        Assert.Empty(list);
    }
}
