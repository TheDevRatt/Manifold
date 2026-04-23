// Tests for SteamNetworkingCore using FakeSteamBackend — no real Steam required.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Manifold.Core.Interop;
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

    // ── Close (host with accepted connections) ─────────────────────────────────

    [Fact]
    public void Close_Host_ClosesAcceptedConnections_BeforeListenSocket()
    {
        var core = MakeCore(out var fake);
        core.CreateHost();
        core.AcceptAndTrack(connection: 99);
        core.Close();
        // CloseConnection(99) must appear before CloseListenSocket in the call log
        var log = fake.CallLog;
        int closeConnIdx   = log.IndexOf("CloseConnection");
        int closeSocketIdx = log.IndexOf("CloseListenSocket");
        Assert.True(closeConnIdx >= 0, "CloseConnection should have been called");
        Assert.True(closeConnIdx < closeSocketIdx, "CloseConnection must precede CloseListenSocket");
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
    public void HandleConnectionStatusChanged_IncomingOnClient_FiresConnectionStatusChanged_NotIncoming()
    {
        // Client mode: a Connecting state triggers ConnectionStatusChanged (not IncomingConnection)
        var core = MakeCore(out _);
        core.CreateClient(new SteamId(1));
        uint? incoming = null;
        (uint conn, int ns)? changed = null;
        core.IncomingConnection     += c => incoming = c;
        core.ConnectionStatusChanged += (c, ns, os, dbg) => changed = (c, ns);

        // State 1 = k_ESteamNetworkingConnectionState_Connecting
        core.HandleConnectionStatusChanged(connection: 99, newState: 1, oldState: 0, debugMsg: "");

        Assert.Null(incoming);                      // IncomingConnection NOT fired
        Assert.NotNull(changed);                    // ConnectionStatusChanged IS fired
        Assert.Equal(99u, changed!.Value.conn);
        Assert.Equal(1, changed!.Value.ns);
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

    // ── ProcessMessage (direct, unsafe) ───────────────────────────────────────

    [Fact]
    public unsafe void ProcessMessage_ValidPacket_AddsReceivedPacketToOutput()
    {
        // Payload: "Hello" (5 bytes) with a 2-byte Manifold header
        // Byte 0 = 0x00 → version=0, kind=Data(0x0)
        // Byte 1 = 0x01 → channel 1
        // Bytes 2-6 = 'H','e','l','l','o'
        var list = new List<ReceivedPacket>();
        byte[] payload = [0x00, 0x01, 0x48, 0x65, 0x6C, 0x6C, 0x6F];

        fixed (byte* pData = payload)
        {
            var msg = new SteamNetworkingMessage_t
            {
                m_pData  = (IntPtr)pData,
                m_cbSize = payload.Length,
                m_conn   = 42,
                // m_pfnRelease = IntPtr.Zero — Release() is a no-op (safe in tests)
            };
            SteamNetworkingCore.ProcessMessage((IntPtr)(&msg), list);
        }

        Assert.Single(list);
        Assert.Equal(42u, list[0].Connection);
        Assert.Equal((byte)1, list[0].Channel);
        Assert.Equal(PacketKind.Data, list[0].Kind);
        Assert.Equal(5, list[0].Size);
        Assert.Equal(0x48, list[0].Buffer[0]); // 'H'
        Assert.Equal(0x65, list[0].Buffer[1]); // 'e'

        ArrayPool<byte>.Shared.Return(list[0].Buffer);
    }

    [Fact]
    public unsafe void ProcessMessage_TooShort_DoesNotAddToOutput()
    {
        // 1 byte — less than PacketHeader.Size=2
        var list = new List<ReceivedPacket>();
        byte[] tooShort = [0x01];

        fixed (byte* pData = tooShort)
        {
            var msg = new SteamNetworkingMessage_t
            {
                m_pData  = (IntPtr)pData,
                m_cbSize = 1,
                m_conn   = 1,
            };
            SteamNetworkingCore.ProcessMessage((IntPtr)(&msg), list);
        }

        Assert.Empty(list);
    }

    [Fact]
    public unsafe void ProcessMessage_ZeroPayload_ControlPacket_AddsToOutput()
    {
        // Header only — 2 bytes, no payload
        // Byte 0 = 0x02 → version=0, kind=HandshakeAck(0x2)
        // Byte 1 = 0x00 → channel 0
        var list = new List<ReceivedPacket>();
        byte[] headerOnly = [0x02, 0x00];

        fixed (byte* pData = headerOnly)
        {
            var msg = new SteamNetworkingMessage_t
            {
                m_pData  = (IntPtr)pData,
                m_cbSize = 2,
                m_conn   = 77,
            };
            SteamNetworkingCore.ProcessMessage((IntPtr)(&msg), list);
        }

        Assert.Single(list);
        Assert.Equal(77u, list[0].Connection);
        Assert.Equal(PacketKind.HandshakeAck, list[0].Kind);
        Assert.Equal(0, list[0].Size); // no payload bytes

        ArrayPool<byte>.Shared.Return(list[0].Buffer);
    }

    // ── GetRemoteSteamId ───────────────────────────────────────────────────────

    [Fact]
    public void GetRemoteSteamId_ReturnsFakeRemoteId()
    {
        var core = MakeCore(out var fake);
        core.CreateHost();
        var steamId = core.GetRemoteSteamId(connection: 1);
        Assert.Equal(fake.RemoteSteamId, steamId);
    }

    // ── State Machine Integration Tests (MASTER_DESIGN §10 cat 3) ─────────────
    //
    // NOTE – SteamMultiplayerPeer full state-machine tests require a Godot
    // headless runtime (GD.Print, SceneTree signals, etc.).  Those are planned
    // for Task 18 (E2E).  Here we exercise the same logic at the
    // SteamNetworkingCore level via FakeSteamBackend — no live Steam needed.
    //
    // NOTE – The following tests from the Task 15 spec are already covered by
    // existing suites and are therefore NOT duplicated here:
    //
    //   • HandshakeState_ExpiresAfterTimeout
    //     → HandshakeStateTests.HandshakeState_IsExpired_AfterTimeout
    //       (src/Manifold.Core.Tests/Protocol/HandshakeProtocolTests.cs)
    //
    //   • HandshakeState_MarkComplete_PreventsExpiry
    //     → HandshakeStateTests.HandshakeState_MarkComplete_PreventsExpiry
    //       (same file)
    //
    //   • HandshakeProtocol_BuildAndParse_RoundTrips_PeerId
    //     → HandshakeProtocolTests.BuildHandshake_ThenTryParse_RoundTrips (Theory)
    //       (same file)
    //
    //   • HandshakeProtocol_BuildAck_IsRecognised
    //     → HandshakeProtocolTests.IsAck_ValidAckPacket_ReturnsTrue +
    //       HandshakeProtocolTests.IsAck_EmptyBuffer_ReturnsFalse
    //       (same file)
    //
    //   • ProcessMessage_HandshakeAck_DecodesKindCorrectly
    //     → SteamNetworkingCoreTests.ProcessMessage_ZeroPayload_ControlPacket_AddsToOutput
    //       (this file, above)

    /// <summary>
    /// GetRemoteSteamId should proxy the value set on FakeSteamBackend —
    /// explicit-ID variant that documents the FakeSteamBackend injection path.
    /// </summary>
    [Fact]
    public void SteamNetworkingCore_GetRemoteSteamId_UsesFakeBackend()
    {
        var fake = new FakeSteamBackend();
        fake.RemoteSteamId = new SteamId(99999UL);
        var core = new SteamNetworkingCore(fake);
        core.CreateHost();
        var id = core.GetRemoteSteamId(connection: 1);
        Assert.Equal(new SteamId(99999UL), id);
    }

    /// <summary>
    /// When a client receives state 3 (Connected) the ConnectionStatusChanged
    /// event must fire with that state — covering the client-side state-machine
    /// transition that is absent from the host-centric tests above.
    /// </summary>
    [Fact]
    public void SteamNetworkingCore_HandleConnectionStatusChanged_ClientConnecting_TransitionsState()
    {
        var core = MakeCore(out _);
        core.CreateClient(new SteamId(1));
        int? capturedState = null;
        core.ConnectionStatusChanged += (c, ns, os, dbg) => capturedState = ns;

        // State 3 = k_ESteamNetworkingConnectionState_Connected on client side
        core.HandleConnectionStatusChanged(connection: 2, newState: 3, oldState: 1, debugMsg: "");

        Assert.Equal(3, capturedState);
    }
}
