// Manifold — SteamPeerRegistry contract tests
// Verifies WeakReference tracking, shutdown fan-out, and exception safety.
// (MASTER_DESIGN §4 Shutdown Contract Step 3)

using System;
using Manifold.Core.Dispatch;
using Xunit;

namespace Manifold.Core.Tests.Contract;

public class SteamPeerRegistryTests
{
    public SteamPeerRegistryTests() => SteamPeerRegistry.ResetForTesting();

    [Fact]
    public void ShutdownAll_CallsForceDisconnect_OnLivePeers()
    {
        var peer = new FakePeer();
        SteamPeerRegistry.Register(peer);
        SteamPeerRegistry.ShutdownAll();
        Assert.True(peer.Disconnected);
    }

    [Fact]
    public void ShutdownAll_DoesNotThrow_WhenPeerForceDisconnectThrows()
    {
        var peer = new ThrowingPeer();
        SteamPeerRegistry.Register(peer);
        var ex = Record.Exception(() => SteamPeerRegistry.ShutdownAll());
        Assert.Null(ex); // exception swallowed safely
    }

    [Fact]
    public void Unregister_RemovesPeer_FromShutdownList()
    {
        var peer = new FakePeer();
        SteamPeerRegistry.Register(peer);
        SteamPeerRegistry.Unregister(peer);
        SteamPeerRegistry.ShutdownAll();
        Assert.False(peer.Disconnected); // not called after unregister
    }

    [Fact]
    public void ShutdownAll_ClearsList_AfterCalling()
    {
        var peer = new FakePeer();
        SteamPeerRegistry.Register(peer);
        SteamPeerRegistry.ShutdownAll();
        SteamPeerRegistry.ShutdownAll(); // second call should not call ForceDisconnect again
        Assert.Equal(1, peer.DisconnectCount);
    }

    private sealed class FakePeer : ISteamPeer
    {
        public bool Disconnected   => DisconnectCount > 0;
        public int  DisconnectCount { get; private set; }
        public void ForceDisconnect() => DisconnectCount++;
    }

    private sealed class ThrowingPeer : ISteamPeer
    {
        public void ForceDisconnect() => throw new InvalidOperationException("Test exception");
    }
}
