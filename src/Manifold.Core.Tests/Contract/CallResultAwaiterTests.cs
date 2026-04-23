// Manifold — CallResultAwaiter<T> contract tests

using System;
using System.Threading;
using System.Threading.Tasks;
using Manifold.Core.Dispatch;
using Manifold.Core.Interop;
using Xunit;

namespace Manifold.Core.Tests.Contract;

// CallResultAwaiter tests share the static CallResultAwaiter registry with
// SteamLifecycleTests (which calls CancelAll on Dispose). Run sequentially.
[Collection("SteamLifecycle")]
public sealed class CallResultAwaiterTests : IDisposable
{
    public CallResultAwaiterTests()  => CallbackDispatcher.ResetForTesting();
    public void Dispose()            => CallbackDispatcher.ResetForTesting();

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Result_DeliveredViaDispatcher_CompletesTask()
    {
        using var awaiter = CallResultAwaiter<SteamServersConnected_t>.Create(
            callHandle: 1234);

        // Simulate Steam delivering the callback
        CallbackDispatcher.InjectCallResultForTest(
            1234, new SteamServersConnected_t(), ioFailed: false);

        await awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(awaiter.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Result_StructFields_AreMarshalledCorrectly()
    {
        using var awaiter = CallResultAwaiter<SteamServerConnectFailure_t>.Create(
            callHandle: 5678);

        var sent = new SteamServerConnectFailure_t
        {
            m_eResult        = 17,
            m_bStillRetrying = true
        };

        CallbackDispatcher.InjectCallResultForTest(5678, sent, ioFailed: false);

        var result = await awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(17, result.m_eResult);
        Assert.True(result.m_bStillRetrying);
    }

    // ── ioFailed path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task IoFailed_True_FaultsWithSteamIOFailedException()
    {
        using var awaiter = CallResultAwaiter<SteamServersConnected_t>.Create(
            callHandle: 8888);

        CallbackDispatcher.InjectCallResultForTest(
            8888, new SteamServersConnected_t(), ioFailed: true);

        await Assert.ThrowsAsync<SteamIOFailedException>(
            () => awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExternalCancellation_CancelsTask()
    {
        using var cts = new CancellationTokenSource();
        using var awaiter = CallResultAwaiter<SteamServersConnected_t>.Create(
            callHandle: 9999, cancellationToken: cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task AfterCancellation_LateCallback_IsIgnored()
    {
        using var cts = new CancellationTokenSource();
        using var awaiter = CallResultAwaiter<SteamServersConnected_t>.Create(
            callHandle: 1111, cancellationToken: cts.Token);

        cts.Cancel();
        // Wait for cancellation to settle
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        // Now deliver callback — must not throw or re-resolve
        CallbackDispatcher.InjectCallResultForTest(
            1111, new SteamServersConnected_t(), ioFailed: false); // must not throw
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Timeout_FaultsWithSteamCallResultTimeoutException()
    {
        using var awaiter = CallResultAwaiter<SteamServersConnected_t>.Create(
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
            callHandle: 3333);

        // Deliver twice — second should be a no-op (handle was removed after first)
        CallbackDispatcher.InjectCallResultForTest(
            3333, new SteamServersConnected_t(), ioFailed: false);
        CallbackDispatcher.InjectCallResultForTest(
            3333, new SteamServersConnected_t(), ioFailed: false);

        // Task must complete exactly once without faulting
        await awaiter.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(awaiter.Task.IsCompletedSuccessfully);
    }
}
