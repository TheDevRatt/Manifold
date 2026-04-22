// Manifold — CallbackDispatcher
// Manual Steam callback dispatcher with type-safe subscriptions and CancelAll().
//
// Design:
//   • Subscriptions are keyed by callback ID (k_iCallback).
//   • Callbacks are dispatched on the pump thread (via Tick()).
//   • Thread-safe: subscribe/unsubscribe from any thread; Tick() is pump-only.
//   • CancelAll() drains all subscriptions — safe to call on shutdown.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Manifold.Core.Dispatch;

/// <summary>
/// Receives raw Steam callback data from the pump thread and routes it to
/// typed C# handlers.
/// </summary>
public sealed class CallbackDispatcher
{
    // ── Internal registration ─────────────────────────────────────────────────

    private readonly ConcurrentDictionary<int, List<ICallbackHandler>> _handlers = new();
    private readonly object _lock = new();

    // ── Raw data queue ────────────────────────────────────────────────────────
    // The pump thread enqueues raw (callbackId, bytes) pairs.
    // Tick() drains the queue on the same pump thread → no marshalling races.

    private readonly ConcurrentQueue<(int Id, byte[] Data)> _queue = new();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a typed callback handler. Returns a token that can be disposed
    /// to unregister.
    /// </summary>
    public IDisposable Subscribe<T>(Action<T> handler) where T : unmanaged
    {
        int id = CallbackId<T>.Value;
        var entry = new TypedHandler<T>(handler, this, id);

        lock (_lock)
        {
            if (!_handlers.TryGetValue(id, out var list))
            {
                list = new List<ICallbackHandler>();
                _handlers[id] = list;
            }
            list.Add(entry);
        }

        return entry;
    }

    /// <summary>
    /// Enqueues a raw callback payload from the pump thread.
    /// Called by <see cref="SteamLifecycle"/> (or tests) after RunCallbacks.
    /// </summary>
    public void Enqueue(int callbackId, byte[] data) =>
        _queue.Enqueue((callbackId, data));

    /// <summary>
    /// Drains the queue and dispatches to registered handlers.
    /// Must be called on the pump thread.
    /// </summary>
    public void Tick()
    {
        while (_queue.TryDequeue(out var item))
        {
            if (!_handlers.TryGetValue(item.Id, out var list))
                continue;

            ICallbackHandler[] snapshot;
            lock (_lock) { snapshot = list.ToArray(); }

            foreach (var handler in snapshot)
                handler.Invoke(item.Data);
        }
    }

    /// <summary>
    /// Unregisters all subscriptions immediately. Safe to call on shutdown.
    /// </summary>
    public void CancelAll()
    {
        lock (_lock)
        {
            foreach (var list in _handlers.Values)
                list.Clear();
            _handlers.Clear();
        }

        // Drain queue so nothing fires after shutdown
        while (_queue.TryDequeue(out _)) { }
    }

    // ── Internal unregister (called by TypedHandler.Dispose) ──────────────────

    internal void Unregister(int id, ICallbackHandler entry)
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(id, out var list))
                list.Remove(entry);
        }
    }

    // ── Nested types ──────────────────────────────────────────────────────────

    internal interface ICallbackHandler
    {
        void Invoke(byte[] data);
    }

    private sealed class TypedHandler<T> : ICallbackHandler, IDisposable where T : unmanaged
    {
        private readonly Action<T> _action;
        private readonly CallbackDispatcher _owner;
        private readonly int _id;
        private bool _disposed;

        internal TypedHandler(Action<T> action, CallbackDispatcher owner, int id)
        {
            _action = action;
            _owner = owner;
            _id = id;
        }

        public unsafe void Invoke(byte[] data)
        {
            if (_disposed) return;
            if (data.Length < sizeof(T)) return;

            fixed (byte* ptr = data)
                _action(*(T*)ptr);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.Unregister(_id, this);
        }
    }
}

/// <summary>
/// Statically resolves the <c>k_iCallback</c> constant from a callback struct.
/// Requires the struct to have a <c>public const int k_iCallback</c> field.
/// </summary>
internal static class CallbackId<T>
{
    // Use a lazy getter instead of a static field initializer so any
    // InvalidOperationException is not wrapped in TypeInitializationException.
    public static int Value => GetId();

    private static int GetId()
    {
        var field = typeof(T).GetField("k_iCallback",
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static);

        if (field is null)
            throw new InvalidOperationException(
                $"{typeof(T).Name} does not define k_iCallback. " +
                "Only generated Steam callback structs are valid type arguments.");

        return (int)field.GetValue(null)!;
    }
}
