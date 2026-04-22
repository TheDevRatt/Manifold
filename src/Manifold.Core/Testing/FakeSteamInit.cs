// Manifold — FakeSteamInit
// Test double for ISteamInit — records calls, supports inject-on-Init failure.

using System;
using System.Threading;

namespace Manifold.Core.Lifecycle;

/// <summary>
/// Test double for <see cref="ISteamInit"/>.
/// Tracks call counts; optionally throws on <see cref="Init"/>.
/// </summary>
public sealed class FakeSteamInit : ISteamInit
{
    public int InitCalls { get; private set; }
    public int RunCallbacksCalls { get; private set; }
    public int ShutdownCalls { get; private set; }

    /// <summary>When set, <see cref="Init"/> throws this exception.</summary>
    public Exception? InitException { get; set; }

    /// <summary>Optional hook invoked during <see cref="RunCallbacks"/>.</summary>
    public Action? OnRunCallbacks { get; set; }

    public void Init()
    {
        InitCalls++;
        if (InitException is not null)
            throw InitException;
    }

    public void RunCallbacks()
    {
        RunCallbacksCalls++;
        OnRunCallbacks?.Invoke();
    }

    public void Shutdown() => ShutdownCalls++;
}
