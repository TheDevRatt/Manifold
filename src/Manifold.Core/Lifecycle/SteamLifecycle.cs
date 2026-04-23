// Manages Steam API init/shutdown and the per-frame RunCallbacks pump.
// MASTER_DESIGN §4 — game-thread manual-dispatch model.

using System;
using System.Diagnostics;
using System.Threading;
using Manifold.Core.Dispatch;

namespace Manifold.Core.Lifecycle;

/// <summary>
/// Manages the lifetime of the Steam API — initialisation, the per-frame callback pump,
/// and orderly shutdown. Only one instance may exist per process lifetime.
/// </summary>
/// <remarks>
/// Call <see cref="Initialize(SteamInitOptions)"/> (static) once per process, then call
/// <see cref="RunCallbacks"/> once per game frame on the game thread. Call
/// <see cref="Dispose"/> on shutdown.
/// Re-initialization after disposal is not permitted.
/// </remarks>
public sealed class SteamLifecycle : IDisposable
{
    // ── Singleton / once-per-process guard ───────────────────────────────────

    /// <summary>The active lifecycle instance, or <c>null</c> when not initialized.</summary>
    public static SteamLifecycle? Current { get; private set; }

    // True once Initialize() has ever been called on any instance (never reset by Dispose).
    private static bool _everInitialized;
    private static readonly object _staticLock = new();

    // ── Instance state ────────────────────────────────────────────────────────

    private bool _disposed;

    private readonly ISteamInit _init;
    private readonly CallbackDispatcher _dispatcher;

    /// <summary>ManagedThreadId of the thread that called Initialize().</summary>
    private int _gameThreadId;

    /// <summary>HSteamPipe captured after successful Init(), used for ManualDispatch.</summary>
    private uint _hSteamPipe;

    // ── Public properties ─────────────────────────────────────────────────────

    /// <summary><c>true</c> after Initialize() succeeds and before Dispose().</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>The AppID of the running application, populated after Initialize().</summary>
    public uint AppId { get; private set; }

    /// <summary>The local user's SteamID, populated after Initialize().</summary>
    public SteamId LocalUser { get; private set; }

    /// <summary>The options that were passed to Initialize().</summary>
    public SteamInitOptions Options { get; private set; } = null!;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised after Steam initialises successfully.</summary>
    public event Action? Initialized;

    /// <summary>Raised after Steam shuts down cleanly.</summary>
    public event Action? Shutdown;

    /// <summary>Raised if a fatal error occurs during initialization.</summary>
    public event Action<Exception>? FatalError;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new lifecycle using the provided Steam init strategy and dispatcher.
    /// Call <see cref="Initialize(SteamInitOptions)"/> on the returned instance (or use
    /// the static <see cref="Initialize(SteamInitOptions)"/> factory) to start Steam.
    /// </summary>
    /// <param name="init">Strategy for SteamAPI calls (production or test double).</param>
    /// <param name="dispatcher">Callback dispatcher this lifecycle drives.</param>
    public SteamLifecycle(ISteamInit init, CallbackDispatcher dispatcher)
    {
        _init       = init       ?? throw new ArgumentNullException(nameof(init));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── Instance Initialize ───────────────────────────────────────────────────

    /// <summary>
    /// Initialises Steam on this instance.
    /// Only one call per process lifetime is allowed — subsequent calls (even after
    /// <see cref="Dispose"/>) always return <see cref="Result{T}.Fail"/>.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    public Result<SteamLifecycle> Initialize(SteamInitOptions options)
    {
        options ??= new SteamInitOptions();

        lock (_staticLock)
        {
            if (_everInitialized)
            {
                if (Current is not null)
                    return Result<SteamLifecycle>.Fail("SteamLifecycle is already initialized.");
                return Result<SteamLifecycle>.Fail(
                    "SteamLifecycle has already been disposed and cannot be re-initialized.");
            }

            _everInitialized = true;
        }

        var result = InternalInitialize(options);
        if (!result.IsSuccess)
        {
            // Allow retry on failure (e.g. steam_appid.txt was absent)
            lock (_staticLock) { _everInitialized = false; }
        }
        return result;
    }

    // ── Static factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Convenience factory: creates a <see cref="SteamLifecycle"/> backed by
    /// <see cref="LiveSteamInit"/> and a fresh <see cref="CallbackDispatcher"/>,
    /// then calls <see cref="Initialize(SteamInitOptions)"/> on it.
    /// Only one call per process lifetime is allowed.
    /// </summary>
    public static Result<SteamLifecycle> CreateAndInitialize(SteamInitOptions options)
    {
        options ??= new SteamInitOptions();
        var lifecycle = new SteamLifecycle(new LiveSteamInit(), new CallbackDispatcher());
        return lifecycle.Initialize(options);
    }

    // ── Per-frame pump ────────────────────────────────────────────────────────

    /// <summary>
    /// Pumps the Steam callback queue. Must be called once per game frame on the game thread.
    /// In DEBUG builds, asserts that this is the same thread that called Initialize().
    /// Safe to call after Dispose() — silently returns.
    /// </summary>
    public void RunCallbacks()
    {
        if (_disposed) return;

        Debug.Assert(
            Thread.CurrentThread.ManagedThreadId == _gameThreadId,
            "RunCallbacks() must be called on the same thread as Initialize().");

        _dispatcher.Tick();
    }

    // ── Disposal / shutdown ───────────────────────────────────────────────────

    /// <summary>
    /// Shuts down Steam in the correct order. Idempotent — calling twice is a no-op.
    /// </summary>
    public void Dispose()
    {
        // Step 1 — guard against double-dispose
        if (_disposed) return;
        _disposed = true;

        // Step 2 — cancel all pending CallResultAwaiter completions
        // (a dedicated CallResultRegistry will be added in a later task;
        //  for now CancelAll handles subscriptions)

        // Step 3 — transition active SteamMultiplayerPeer instances (stub/no-op for now)
        // SteamPeerRegistry.ShutdownAll();

        // Step 4 — clear all CallbackDispatcher handler registrations
        _dispatcher.CancelAll();

        // Step 5 — call SteamAPI_Shutdown
        if (IsInitialized)
        {
            _init.Shutdown();
        }

        IsInitialized = false;

        // Step 6 — clear Current
        lock (_staticLock)
        {
            Current = null;
        }

        Shutdown?.Invoke();
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private Result<SteamLifecycle> InternalInitialize(SteamInitOptions options)
    {
        Options       = options;
        _gameThreadId = Thread.CurrentThread.ManagedThreadId;

        try
        {
            // Optionally restart via Steam launcher
            if (options.AllowRestart && options.AppId != 0)
            {
                if (Interop.SteamNative.SteamAPI_RestartAppIfNecessary(options.AppId))
                    return Result<SteamLifecycle>.Fail(
                        "Steam relaunched the application through Steam. Exiting current process.");
            }

            bool ok = _init.Init();
            if (!ok)
                return Result<SteamLifecycle>.Fail(
                    "SteamAPI_Init() returned false. Ensure Steam is running and steam_appid.txt is present.");

            if (options.ManualDispatch)
                _init.ManualDispatchInit();

            // Populate properties
            _hSteamPipe   = _init.GetHSteamPipe();
            AppId         = _init.GetAppId();
            LocalUser     = new SteamId(_init.GetLocalSteamId());
            IsInitialized = true;

            lock (_staticLock)
            {
                Current = this;
            }

            Initialized?.Invoke();
            return Result<SteamLifecycle>.Ok(this);
        }
        catch (Exception ex)
        {
            IsInitialized = false;
            FatalError?.Invoke(ex);
            return Result<SteamLifecycle>.Fail(ex.Message);
        }
    }
}
