// Bridges a Steam SteamAPICall_t handle to a C# Task<T> with CancellationToken + timeout.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Manifold.Core.Dispatch;

/// <summary>
/// Non-generic static registry for <see cref="CallResultAwaiter{T}"/> instances.
/// Allows all pending awaiters to be faulted at once (e.g., on Steam shutdown).
/// </summary>
public static class CallResultAwaiter
{
    private static readonly List<Action<Exception>> _activeCancellers = new();
    private static readonly object _cancelLock = new();

    internal static void RegisterCanceller(Action<Exception> cancel)
    {
        lock (_cancelLock) _activeCancellers.Add(cancel);
    }

    internal static void UnregisterCanceller(Action<Exception> cancel)
    {
        lock (_cancelLock) _activeCancellers.Remove(cancel);
    }

    /// <summary>
    /// Faults all pending <see cref="CallResultAwaiter{T}"/> tasks with the given exception.
    /// Clears the registry. Safe to call multiple times (idempotent after first call empties it).
    /// </summary>
    /// <param name="reason">The exception to fault pending tasks with.</param>
    public static void CancelAll(Exception reason)
    {
        List<Action<Exception>> snapshot;
        lock (_cancelLock)
        {
            snapshot = new(_activeCancellers);
            _activeCancellers.Clear();
        }
        foreach (var c in snapshot) c(reason);
    }
}

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
    private readonly Action<Exception> _canceller;
    private int _settled; // 0 = pending, 1 = settled (CAS guard)

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

        // Register global shutdown canceller — faults TCS with the provided exception
        _canceller = ex => TrySettle(() => _tcs.TrySetException(ex));
        CallResultAwaiter.RegisterCanceller(_canceller);
    }

    /// <summary>
    /// The task that completes when the Steam call result arrives,
    /// or faults/cancels if timeout or cancellation fires first.
    /// </summary>
    public Task<T> Task => _tcs.Task;

    /// <inheritdoc/>
    public void Dispose()
    {
        CallResultAwaiter.UnregisterCanceller(_canceller);
        _subscription.Dispose();
        _ctReg.Dispose();
        _timeoutCts?.Dispose();
    }

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
