// Manifold — CallResultAwaiter<T> contract tests

using System;
using System.Threading;
using System.Threading.Tasks;
using Manifold.Core.Dispatch;
using Manifold.Core.Interop;
using Xunit;

namespace Manifold.Core.Tests.Contract;

public sealed class CallResultAwaiterTests
{
    private readonly CallbackDispatcher _dispatcher = new();

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Result_DeliveredViaDispatcher_CompletesTask()
    {
        using var awaiter = CallResultAwaiter<SteamServersConnected_t>.Create(
            _dispatcher, callHandle: 1234);

        // Simulate Steam delivering the callback
        var data = MakeBytes(new SteamServersConnected_t());
        _dispatcher.Enqueue(SteamServersConnected_t.k_iCallback, data);
        _dispatcher.Tick();

        var result = await awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull((object?)result);
    }

    [Fact]
    public async Task Result_StructFields_AreMarshalledCorrectly()
    {
        using var awaiter = CallResultAwaiter<SteamServerConnectFailure_t>.Create(
            _dispatcher, callHandle: 5678);

        var sent = new SteamServerConnectFailure_t
        {
            m_eResult = 17,
            m_bStillRetrying = true
        };

        _dispatcher.Enqueue(SteamServerConnectFailure_t.k_iCallback, MakeBytes(sent));
        _dispatcher.Tick();

        var result = await awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(17, result.m_eResult);
        Assert.True(result.m_bStillRetrying);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExternalCancellation_CancelsTask()
    {
        using var cts = new CancellationTokenSource();
        using var awaiter = CallResultAwaiter<SteamServersConnected_t>.Create(
            _dispatcher, callHandle: 9999, cancellationToken: cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task AfterCancellation_LateCallback_IsIgnored()
    {
        using var cts = new CancellationTokenSource();
        using var awaiter = CallResultAwaiter<SteamServersConnected_t>.Create(
            _dispatcher, callHandle: 1111, cancellationToken: cts.Token);

        cts.Cancel();
        // Wait for cancellation to settle
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        // Now deliver callback — must not throw or re-resolve
        _dispatcher.Enqueue(SteamServersConnected_t.k_iCallback,
            MakeBytes(new SteamServersConnected_t()));
        _dispatcher.Tick(); // must not throw
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Timeout_FaultsWithSteamCallResultTimeoutException()
    {
        using var awaiter = CallResultAwaiter<SteamServersConnected_t>.Create(
            _dispatcher,
            callHandle: 2222,
            timeout: TimeSpan.FromMilliseconds(50));

        var ex = await Assert.ThrowsAsync<SteamCallResultTimeoutException>(
            () => awaiter.Task.WaitAsync(TimeSpan.FromSeconds(3)));

        Assert.Equal(2222UL, ex.ApiCall);
    }

    // ── Settlement is idempotent ──────────────────────────────────────────────

    [Fact]
    public async Task DoubleDelivery_OnlySettlesOnce()
    {
        using var awaiter = CallResultAwaiter<SteamServersConnected_t>.Create(
            _dispatcher, callHandle: 3333);

        var data = MakeBytes(new SteamServersConnected_t());

        // Deliver twice
        _dispatcher.Enqueue(SteamServersConnected_t.k_iCallback, data);
        _dispatcher.Enqueue(SteamServersConnected_t.k_iCallback, data);
        _dispatcher.Tick();

        // Task must complete exactly once without faulting
        var result = await awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull((object?)result);
        Assert.True(awaiter.Task.IsCompletedSuccessfully);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static unsafe byte[] MakeBytes<T>(T value) where T : unmanaged
    {
        var bytes = new byte[sizeof(T)];
        fixed (byte* ptr = bytes)
            *(T*)ptr = value;
        return bytes;
    }
}
