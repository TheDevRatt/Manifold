// Manages Steam API init/shutdown and the RunCallbacks pump.

using System;
using System.Threading;
using Manifold.Core.Dispatch;

namespace Manifold.Core.Lifecycle;

/// <summary>
/// Manages the lifetime of the Steam API — initialisation, the callback pump,
/// and orderly shutdown.  Only one instance may be active at a time.
/// </summary>
public sealed class SteamLifecycle : IDisposable
{
    public enum State
    {
        Uninitialized,
        Initializing,
        Running,
        ShuttingDown,
        Stopped
    }

    private int _state = (int)State.Uninitialized;

    /// <summary>Current lifecycle state.</summary>
    public State CurrentState => (State)Volatile.Read(ref _state);

    private static SteamLifecycle? _active;

    private readonly ISteamInit _init;
    private readonly CallbackDispatcher _dispatcher;

    private Thread? _pumpThread;
    private readonly CancellationTokenSource _cts = new();
    private TimeSpan _pumpInterval = TimeSpan.FromMilliseconds(15);

    /// <summary>Raised after Steam initialises successfully.</summary>
    public event Action? Initialized;

    /// <summary>Raised after Steam shuts down cleanly.</summary>
    public event Action? Shutdown;

    /// <summary>Raised if Steam init or the pump throws an unhandled exception.</summary>
    public event Action<Exception>? FatalError;

    /// <param name="init">Strategy for SteamAPI_Init / SteamAPI_Shutdown calls.</param>
    /// <param name="dispatcher">Callback dispatcher this lifecycle drives.</param>
    public SteamLifecycle(ISteamInit init, CallbackDispatcher dispatcher)
    {
        _init = init ?? throw new ArgumentNullException(nameof(init));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// Initialises Steam and starts the RunCallbacks pump on a background thread.
    /// </summary>
    /// <param name="pumpInterval">How often to call RunCallbacks (default 15 ms).</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if another <see cref="SteamLifecycle"/> is already active or this
    /// instance is not in the <see cref="State.Uninitialized"/> state.
    /// </exception>
    /// <exception cref="SteamInitException">Thrown if SteamAPI_Init fails.</exception>
    public void Start(TimeSpan? pumpInterval = null)
    {
        // Singleton guard
        var prev = Interlocked.CompareExchange(ref _active, this, null);
        if (prev is not null && !ReferenceEquals(prev, this))
            throw new InvalidOperationException(
                "Another SteamLifecycle is already active. Dispose it before creating a new one.");

        // State guard
        if (Interlocked.CompareExchange(ref _state, (int)State.Initializing, (int)State.Uninitialized)
            != (int)State.Uninitialized)
        {
            Interlocked.CompareExchange(ref _active, null, this);
            throw new InvalidOperationException(
                $"Cannot Start from state {CurrentState}.");
        }

        if (pumpInterval.HasValue)
            _pumpInterval = pumpInterval.Value;

        try
        {
            _init.Init(); // throws SteamInitException on failure
        }
        catch
        {
            Volatile.Write(ref _state, (int)State.Stopped);
            Interlocked.CompareExchange(ref _active, null, this);
            throw;
        }

        Volatile.Write(ref _state, (int)State.Running);
        Initialized?.Invoke();

        _pumpThread = new Thread(PumpLoop)
        {
            Name = "Manifold.SteamCallbackPump",
            IsBackground = true
        };
        _pumpThread.Start();
    }

    /// <summary>
    /// Signals shutdown and blocks until the pump thread exits cleanly.
    /// Safe to call from any thread.
    /// </summary>
    public void Stop()
    {
        if (Interlocked.CompareExchange(ref _state, (int)State.ShuttingDown, (int)State.Running)
            != (int)State.Running)
            return; // already stopped or never started

        _cts.Cancel();
        _pumpThread?.Join(TimeSpan.FromSeconds(5));

        _dispatcher.CancelAll();
        _init.Shutdown();

        Volatile.Write(ref _state, (int)State.Stopped);
        Interlocked.CompareExchange(ref _active, null, this);
        Shutdown?.Invoke();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    private void PumpLoop()
    {
        var token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                _init.RunCallbacks();
                _dispatcher.Tick();
            }
            catch (Exception ex)
            {
                // Fatal pump error — clean up without calling Stop() to avoid
                // joining the current thread (which would deadlock for 5s).
                if (Interlocked.CompareExchange(ref _state, (int)State.ShuttingDown, (int)State.Running)
                    == (int)State.Running)
                {
                    _dispatcher.CancelAll();
                    _init.Shutdown();
                    Volatile.Write(ref _state, (int)State.Stopped);
                    Interlocked.CompareExchange(ref _active, null, this);
                    Shutdown?.Invoke();
                }

                FatalError?.Invoke(ex);
                return;
            }

            try
            {
                Task.Delay(_pumpInterval, token).Wait(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
