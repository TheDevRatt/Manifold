// Manifold — CallResultAwaiter<T>
// Bridges a Steam SteamAPICall_t handle to a C# Task<T> with CancellationToken + timeout.
//
// Design:
//   • Each awaiter registers a one-shot subscription on CallbackDispatcher.
//   • On Tick(), the dispatcher routes the result callback to Resolve().
//   • On cancellation or timeout, the TaskCompletionSource is cancelled/faulted
//     and the dispatcher subscription is dropped immediately.
//   • Thread-safe: Resolve() and Cancel() are both safe from any thread.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Manifold.Core.Dispatch;

/// <summary>
/// Awaits a Steam async call result identified by a <c>SteamAPICall_t</c> handle.
/// </summary>
/// <typeparam name="T">
/// The callback struct that carries the result (must define <c>k_iCallback</c>).
/// </typeparam>
public sealed class CallResultAwaiter<T> : IDisposable where T : unmanaged
{
    private readonly TaskCompletionSource<T> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ulong _callHandle;
    private readonly IDisposable _subscription;
    private readonly CancellationTokenRegistration _ctReg;
    private readonly CancellationTokenSource? _timeoutCts;
    private int _settled; // 0 = pending, 1 = settled (CAS guard)

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an awaiter and registers it with <paramref name="dispatcher"/>.
    /// </summary>
    /// <param name="dispatcher">The dispatcher that will route the result callback.</param>
    /// <param name="callHandle">The <c>SteamAPICall_t</c> handle returned by the Steam API.</param>
    /// <param name="cancellationToken">Optional external cancellation.</param>
    /// <param name="timeout">Optional timeout (defaults to 30 seconds if omitted).</param>
    public static CallResultAwaiter<T> Create(
        CallbackDispatcher dispatcher,
        ulong callHandle,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        return new CallResultAwaiter<T>(dispatcher, callHandle, cancellationToken, timeout);
    }

    // ── Construction ──────────────────────────────────────────────────────────

    private CallResultAwaiter(
        CallbackDispatcher dispatcher,
        ulong callHandle,
        CancellationToken cancellationToken,
        TimeSpan? timeout)
    {
        _callHandle = callHandle;

        // Subscribe to the result callback type — filtered to our call handle
        _subscription = dispatcher.Subscribe<T>(OnCallback);

        // Timeout: default 30s
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        _timeoutCts = new CancellationTokenSource(effectiveTimeout);

        // Timeout fires → SteamCallResultTimeoutException
        var capturedTimeout = effectiveTimeout;
        var capturedHandle = callHandle;
        _timeoutCts.Token.Register(() =>
            TrySettle(() =>
                _tcs.TrySetException(
                    new SteamCallResultTimeoutException(capturedHandle, capturedTimeout))),
            useSynchronizationContext: false);

        // External cancellation fires → TaskCanceledException
        if (cancellationToken.CanBeCanceled)
        {
            _ctReg = cancellationToken.Register(() =>
                TrySettle(() => _tcs.TrySetCanceled()),
                useSynchronizationContext: false);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// The task that completes when the Steam call result arrives,
    /// or faults/cancels if timeout or cancellation fires first.
    /// </summary>
    public Task<T> Task => _tcs.Task;

    /// <inheritdoc/>
    public void Dispose()
    {
        _subscription.Dispose();
        _ctReg.Dispose();
        _timeoutCts?.Dispose();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnCallback(T result)
    {
        TrySettle(() => _tcs.TrySetResult(result));
    }

    private void TrySettle(Action action)
    {
        if (Interlocked.CompareExchange(ref _settled, 1, 0) != 0) return;
        action();
        Dispose();
    }
}
