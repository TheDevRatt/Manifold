// Manifold — CallbackDispatcher contract tests

using System;
using System.Collections.Generic;
using System.Threading;
using Manifold.Core.Dispatch;
using Manifold.Core.Interop;
using Xunit;

namespace Manifold.Core.Tests.Contract;

public sealed class CallbackDispatcherTests
{
    private readonly CallbackDispatcher _dispatcher = new();

    // ── Basic subscribe + dispatch ────────────────────────────────────────────

    [Fact]
    public void Subscribe_AndEnqueue_DispatchesCallback()
    {
        SteamServersConnected_t? received = null;
        _dispatcher.Subscribe<SteamServersConnected_t>(cb => received = cb);

        var data = MakeBytes<SteamServersConnected_t>(new SteamServersConnected_t());
        _dispatcher.Enqueue(SteamServersConnected_t.k_iCallback, data);
        _dispatcher.Tick();

        Assert.NotNull(received);
    }

    [Fact]
    public void UnknownCallbackId_IsIgnored()
    {
        // Enqueue with an id no one is subscribed to — should not throw
        _dispatcher.Enqueue(99999, new byte[8]);
        _dispatcher.Tick(); // must not throw
    }

    [Fact]
    public void MultipleSubscribers_SameCallback_BothFire()
    {
        int count = 0;
        _dispatcher.Subscribe<SteamServersConnected_t>(_ => count++);
        _dispatcher.Subscribe<SteamServersConnected_t>(_ => count++);

        var data = MakeBytes<SteamServersConnected_t>(new SteamServersConnected_t());
        _dispatcher.Enqueue(SteamServersConnected_t.k_iCallback, data);
        _dispatcher.Tick();

        Assert.Equal(2, count);
    }

    // ── Unsubscribe (dispose token) ───────────────────────────────────────────

    [Fact]
    public void Dispose_Token_UnsubscribesHandler()
    {
        int count = 0;
        var token = _dispatcher.Subscribe<SteamServersConnected_t>(_ => count++);
        token.Dispose();

        var data = MakeBytes<SteamServersConnected_t>(new SteamServersConnected_t());
        _dispatcher.Enqueue(SteamServersConnected_t.k_iCallback, data);
        _dispatcher.Tick();

        Assert.Equal(0, count);
    }

    [Fact]
    public void DoubleDispose_Token_IsIdempotent()
    {
        var token = _dispatcher.Subscribe<SteamServersConnected_t>(_ => { });
        token.Dispose();
        token.Dispose(); // must not throw
    }

    // ── CancelAll ─────────────────────────────────────────────────────────────

    [Fact]
    public void CancelAll_PreventsDispatch()
    {
        int count = 0;
        _dispatcher.Subscribe<SteamServersConnected_t>(_ => count++);

        var data = MakeBytes<SteamServersConnected_t>(new SteamServersConnected_t());
        _dispatcher.Enqueue(SteamServersConnected_t.k_iCallback, data);

        _dispatcher.CancelAll();
        _dispatcher.Tick(); // queue was drained, handlers removed

        Assert.Equal(0, count);
    }

    [Fact]
    public void CancelAll_DrainsPendingQueue()
    {
        _dispatcher.Enqueue(1, new byte[4]);
        _dispatcher.Enqueue(2, new byte[4]);
        _dispatcher.CancelAll();

        // Tick should process nothing
        int count = 0;
        _dispatcher.Subscribe<SteamServersConnected_t>(_ => count++);
        _dispatcher.Tick();

        Assert.Equal(0, count);
    }

    // ── Data integrity ────────────────────────────────────────────────────────

    [Fact]
    public unsafe void StructFields_AreMarshalledCorrectly()
    {
        SteamServerConnectFailure_t? received = null;
        _dispatcher.Subscribe<SteamServerConnectFailure_t>(cb => received = cb);

        var original = new SteamServerConnectFailure_t
        {
            m_eResult = 42,
            m_bStillRetrying = true
        };
        var data = MakeBytes(original);
        _dispatcher.Enqueue(SteamServerConnectFailure_t.k_iCallback, data);
        _dispatcher.Tick();

        Assert.NotNull(received);
        Assert.Equal(42, received!.Value.m_eResult);
        Assert.True(received.Value.m_bStillRetrying);
    }

    // ── Struct without k_iCallback ────────────────────────────────────────────

    [Fact]
    public void Subscribe_StructWithoutCallbackId_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _dispatcher.Subscribe<BadStruct>(_ => { }));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static unsafe byte[] MakeBytes<T>(T value) where T : unmanaged
    {
        var bytes = new byte[sizeof(T)];
        fixed (byte* ptr = bytes)
            *(T*)ptr = value;
        return bytes;
    }

    private struct BadStruct { public int X; } // no k_iCallback
}
