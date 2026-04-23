// Manifold — CallbackDispatcher contract tests (MASTER_DESIGN §7.1)
//
// CallbackDispatcher is now an internal static class. Tests exercise it via:
//   - CallbackDispatcher.Register / Subscribe / Unregister
//   - CallbackDispatcher.InjectForTest  — bypasses ManualDispatch; routes directly to handlers
//   - CallbackDispatcher.ResetForTesting — isolates state between tests

using System;
using System.Runtime.InteropServices;
using Manifold.Core.Dispatch;
using Manifold.Core.Interop;
using Xunit;

namespace Manifold.Core.Tests.Contract;

public sealed class CallbackDispatcherTests : IDisposable
{
    public CallbackDispatcherTests()  => CallbackDispatcher.ResetForTesting();
    public void Dispose()             => CallbackDispatcher.ResetForTesting();

    // ── Basic subscribe + inject ───────────────────────────────────────────────

    [Fact]
    public unsafe void Subscribe_AndInject_DispatchesCallback()
    {
        SteamServersConnected_t? received = null;
        using var _ = CallbackDispatcher.Subscribe<SteamServersConnected_t>(cb => received = cb);

        var value = new SteamServersConnected_t();
        CallbackDispatcher.InjectForTest(
            SteamServersConnected_t.k_iCallback,
            new IntPtr(&value));

        Assert.NotNull(received);
    }

    [Fact]
    public unsafe void UnknownCallbackId_IsIgnored()
    {
        var value = new SteamServersConnected_t();
        // Should not throw
        CallbackDispatcher.InjectForTest(99999, new IntPtr(&value));
    }

    [Fact]
    public unsafe void MultipleSubscribers_SameCallback_BothFire()
    {
        int count = 0;
        using var a = CallbackDispatcher.Subscribe<SteamServersConnected_t>(_ => count++);
        using var b = CallbackDispatcher.Subscribe<SteamServersConnected_t>(_ => count++);

        var value = new SteamServersConnected_t();
        CallbackDispatcher.InjectForTest(
            SteamServersConnected_t.k_iCallback,
            new IntPtr(&value));

        Assert.Equal(2, count);
    }

    // ── Unsubscribe (dispose token) ────────────────────────────────────────────

    [Fact]
    public unsafe void DisposeToken_UnsubscribesHandler()
    {
        int count = 0;
        var token = CallbackDispatcher.Subscribe<SteamServersConnected_t>(_ => count++);
        token.Dispose();

        var value = new SteamServersConnected_t();
        CallbackDispatcher.InjectForTest(
            SteamServersConnected_t.k_iCallback,
            new IntPtr(&value));

        Assert.Equal(0, count);
    }

    [Fact]
    public void DoubleDispose_Token_IsIdempotent()
    {
        var token = CallbackDispatcher.Subscribe<SteamServersConnected_t>(_ => { });
        token.Dispose();
        token.Dispose(); // must not throw
    }

    // ── CancelAll ──────────────────────────────────────────────────────────────

    [Fact]
    public unsafe void CancelAll_PreventsSubsequentDispatch()
    {
        int count = 0;
        using var _ = CallbackDispatcher.Subscribe<SteamServersConnected_t>(_ => count++);

        CallbackDispatcher.CancelAll(new InvalidOperationException("test"));

        var value = new SteamServersConnected_t();
        CallbackDispatcher.InjectForTest(
            SteamServersConnected_t.k_iCallback,
            new IntPtr(&value));

        Assert.Equal(0, count);
    }

    // ── Data integrity ─────────────────────────────────────────────────────────

    [Fact]
    public unsafe void StructFields_AreMarshalledCorrectly()
    {
        SteamServerConnectFailure_t? received = null;
        using var _ = CallbackDispatcher.Subscribe<SteamServerConnectFailure_t>(cb => received = cb);

        var original = new SteamServerConnectFailure_t
        {
            m_eResult        = 42,
            m_bStillRetrying = true
        };
        CallbackDispatcher.InjectForTest(
            SteamServerConnectFailure_t.k_iCallback,
            new IntPtr(&original));

        Assert.NotNull(received);
        Assert.Equal(42, received!.Value.m_eResult);
        Assert.True(received.Value.m_bStillRetrying);
    }

    // ── Struct without k_iCallback ────────────────────────────────────────────

    [Fact]
    public void Subscribe_StructWithoutCallbackId_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CallbackDispatcher.Subscribe<BadStruct>(_ => { }));
    }

    private struct BadStruct { public int X; } // no k_iCallback
}
