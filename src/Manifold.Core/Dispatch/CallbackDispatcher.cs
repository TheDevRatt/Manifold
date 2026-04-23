// Routes raw Steam callback data to typed C# handlers.
// MASTER_DESIGN §7.1 — internal static class using SteamAPI_ManualDispatch_*

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Manifold.Core.Interop;

namespace Manifold.Core.Dispatch;

/// <summary>
/// Routes Steam callback data from <c>SteamAPI_ManualDispatch_*</c> to typed C# handlers.
/// Called on the game thread once per frame via <see cref="Tick"/>.
/// </summary>
/// <remarks>
/// This is an <c>internal static class</c>; all state is process-wide.
/// <see cref="SteamLifecycle"/> owns the lifetime: it calls <see cref="Tick"/> each frame and
/// <see cref="CancelAll"/> on shutdown.
/// </remarks>
internal static class CallbackDispatcher
{
    private static readonly object _lock = new();
    private static readonly Dictionary<int, List<Action<IntPtr>>> _handlers = new();

    private record CallResultRegistration(Action<IntPtr, bool> Handler, int ExpectedCallbackId, int DataSize);
    private static readonly Dictionary<ulong, CallResultRegistration> _callResults = new();

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>
    /// ManagedThreadId of the game thread. Set by <see cref="SteamLifecycle"/> at
    /// initialisation time. Used by <see cref="DebugAssertMainThread"/> in DEBUG builds.
    /// </summary>
    internal static int GameThreadId { get; set; }

    // ── Callback registration ─────────────────────────────────────────────────

    /// <summary>
    /// Registers a raw <see cref="IntPtr"/> handler for <paramref name="callbackId"/>.
    /// The pointer passed to the handler points to the callback struct data.
    /// </summary>
    internal static IDisposable Register(int callbackId, Action<IntPtr> handler)
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(callbackId, out var list))
                _handlers[callbackId] = list = new List<Action<IntPtr>>();
            list.Add(handler);
        }
        return new SubscriptionToken(callbackId, handler);
    }

    /// <summary>Removes a previously registered handler.</summary>
    internal static void Unregister(int callbackId, Action<IntPtr> handler)
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(callbackId, out var list))
                list.Remove(handler);
        }
    }

    /// <summary>
    /// Subscribes to a typed Steam callback. Returns a token whose <see cref="IDisposable.Dispose"/>
    /// unregisters the handler.
    /// </summary>
    internal static unsafe IDisposable Subscribe<T>(Action<T> handler) where T : unmanaged
    {
        int id = CallbackId<T>.Value;
        void RawHandler(IntPtr ptr) => handler(*(T*)ptr);
        return Register(id, RawHandler);
    }

    // ── Call-result registration ──────────────────────────────────────────────

    /// <summary>
    /// Registers a handler to be invoked when the call result for <paramref name="apiCall"/>
    /// arrives. The dispatcher calls <c>SteamAPI_ManualDispatch_GetAPICallResult</c> to fill
    /// a result buffer, then invokes <paramref name="handler"/> with a pointer to that buffer
    /// and the <c>ioFailed</c> flag.
    /// </summary>
    internal static void RegisterCallResult(
        ulong apiCall,
        Action<IntPtr, bool> handler,
        int expectedCallbackId,
        int dataSize)
    {
        lock (_lock)
            _callResults[apiCall] = new CallResultRegistration(handler, expectedCallbackId, dataSize);
    }

    /// <summary>Removes the pending call result registration for <paramref name="apiCall"/>.</summary>
    internal static void CancelCallResult(ulong apiCall)
    {
        lock (_lock) _callResults.Remove(apiCall);
    }

    /// <summary>
    /// Clears all handler registrations and pending call result entries.
    /// Called by <see cref="SteamLifecycle"/> during Dispose.
    /// </summary>
    internal static void CancelAll(Exception reason)
    {
        List<(CallResultRegistration, ulong)> pending;
        lock (_lock)
        {
            pending = _callResults.Select(kv => (kv.Value, kv.Key)).ToList();
            _handlers.Clear();
            _callResults.Clear();
        }
        // Fault all CallResultAwaiter TCS objects with the supplied reason
        CallResultAwaiter.CancelAll(reason);

        // Also invoke the raw Action<IntPtr, bool> handlers with ioFailed=true so
        // any non-TCS consumers (future use) also receive a failure signal.
        foreach (var (reg, _) in pending)
            reg.Handler(IntPtr.Zero, true);
    }

    // ── Per-frame pump ────────────────────────────────────────────────────────

    [Conditional("DEBUG")]
    private static void DebugAssertMainThread()
    {
        Debug.Assert(
            Thread.CurrentThread.ManagedThreadId == GameThreadId,
            "CallbackDispatcher.Tick() must be called on the game thread.");
    }

    /// <summary>
    /// Pumps the Steam callback queue via <c>SteamAPI_ManualDispatch_*</c> and routes
    /// each callback to registered handlers. Must be called on the game thread.
    /// </summary>
    internal static unsafe void Tick(uint hSteamPipe)
    {
        DebugAssertMainThread();
        SteamNative.SteamAPI_ManualDispatch_RunFrame(hSteamPipe);

        while (SteamNative.SteamAPI_ManualDispatch_GetNextCallback(hSteamPipe, out var msg))
        {
            try
            {
                if (msg.m_iCallback == SteamAPICallCompleted_t.k_iCallback)
                {
                    // m_pubParam → SteamAPICallCompleted_t, read the async call handle
                    var completed = *(SteamAPICallCompleted_t*)msg.m_pubParam;
                    ulong apiCallHandle = completed.m_hAsyncCall;

                    CallResultRegistration? reg = null;
                    lock (_lock)
                    {
                        if (_callResults.TryGetValue(apiCallHandle, out reg))
                            _callResults.Remove(apiCallHandle);
                    }

                    if (reg is not null)
                    {
                        byte[] buf = new byte[reg.DataSize];
                        fixed (byte* pBuf = buf)
                        {
                            SteamNative.SteamAPI_ManualDispatch_GetAPICallResult(
                                hSteamPipe, apiCallHandle, (IntPtr)pBuf,
                                reg.DataSize, reg.ExpectedCallbackId, out bool ioFailed);
                            try { reg.Handler((IntPtr)pBuf, ioFailed); }
                            catch (Exception)
                            {
                                // Handler exceptions are swallowed to prevent one subscriber from aborting the frame pump.
                                // Callers should handle exceptions within their own handlers.
                            }
                        }
                    }
                }
                else
                {
                    Action<IntPtr>[]? snapshot = null;
                    lock (_lock)
                    {
                        if (_handlers.TryGetValue(msg.m_iCallback, out var list))
                            snapshot = list.ToArray();
                    }
                    if (snapshot is not null)
                        foreach (var h in snapshot)
                        {
                            try { h(msg.m_pubParam); }
                            catch (Exception)
                            {
                                // Handler exceptions are swallowed to prevent one subscriber from aborting the frame pump.
                                // Callers should handle exceptions within their own handlers.
                            }
                        }
                }
            }
            finally
            {
                SteamNative.SteamAPI_ManualDispatch_FreeLastCallback(hSteamPipe);
            }
        }
    }

    // ── Test seams ────────────────────────────────────────────────────────────

    /// <summary>
    /// For testing only: directly dispatches <paramref name="data"/> to all handlers
    /// registered for <paramref name="callbackId"/>, bypassing ManualDispatch.
    /// </summary>
    internal static void InjectForTest(int callbackId, IntPtr data)
    {
        Action<IntPtr>[]? snapshot = null;
        lock (_lock)
        {
            if (_handlers.TryGetValue(callbackId, out var list))
                snapshot = list.ToArray();
        }
        if (snapshot is null) return;
        foreach (var h in snapshot)
        {
            try { h(data); }
            catch (Exception) { /* swallow — one bad handler must not abort the test-injection loop */ }
        }
    }

    /// <summary>
    /// For testing only: directly invokes the call result handler registered for
    /// <paramref name="apiCallHandle"/>, bypassing ManualDispatch and
    /// <c>GetAPICallResult</c>.
    /// </summary>
    internal static unsafe void InjectCallResultForTest<T>(ulong apiCallHandle, T value, bool ioFailed)
        where T : unmanaged
    {
        CallResultRegistration? reg = null;
        lock (_lock)
        {
            if (_callResults.TryGetValue(apiCallHandle, out reg))
                _callResults.Remove(apiCallHandle);
        }
        if (reg is null) return;
        byte[] buf = new byte[sizeof(T)];
        fixed (byte* p = buf)
        {
            *(T*)p = value;
            reg.Handler((IntPtr)p, ioFailed);
        }
    }

    /// <summary>Resets all static state. For test isolation only.</summary>
    internal static void ResetForTesting()
    {
        lock (_lock)
        {
            _handlers.Clear();
            _callResults.Clear();
            GameThreadId = 0;
        }
        CallResultAwaiter.ResetForTesting();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class SubscriptionToken : IDisposable
    {
        private readonly int          _callbackId;
        private readonly Action<IntPtr> _handler;
        private bool _disposed;

        internal SubscriptionToken(int callbackId, Action<IntPtr> handler)
        {
            _callbackId = callbackId;
            _handler    = handler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unregister(_callbackId, _handler);
        }
    }
}

/// <summary>
/// Statically resolves the <c>k_iCallback</c> constant from a callback struct.
/// Requires the struct to have a <c>public const int k_iCallback</c> field.
/// </summary>
internal static class CallbackId<T>
{
    // Use a getter instead of a static field initializer so any
    // InvalidOperationException is not wrapped in TypeInitializationException.
    internal static int Value => _cached ??= GetId();
    private static int? _cached;

    private static int GetId()
    {
        var field = typeof(T).GetField("k_iCallback",
            System.Reflection.BindingFlags.Public  |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static);

        if (field is null)
            throw new InvalidOperationException(
                $"{typeof(T).Name} does not define k_iCallback. " +
                "Only generated Steam callback structs are valid type arguments.");

        return (int)field.GetValue(null)!;
    }
}
