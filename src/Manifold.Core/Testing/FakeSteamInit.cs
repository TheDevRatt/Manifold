// Manifold — FakeSteamInit
// Test double for ISteamInit — records calls, supports inject-on-Init failure.

using System;

namespace Manifold.Core.Lifecycle;

/// <summary>
/// Test double for <see cref="ISteamInit"/>.
/// Tracks call counts; optionally makes <see cref="Init"/> return <c>false</c> or throw.
/// </summary>
public sealed class FakeSteamInit : ISteamInit
{
    public int InitCalls { get; private set; }
    public int RunCallbacksCalls { get; private set; }
    public int ShutdownCalls { get; private set; }
    public int ManualDispatchInitCalls { get; private set; }

    /// <summary>
    /// When set, <see cref="Init"/> throws this exception instead of returning false/true.
    /// </summary>
    public Exception? InitException { get; set; }

    /// <summary>
    /// When set to false, <see cref="Init"/> returns <c>false</c> (simulating Steam not running).
    /// Default: <c>true</c>.
    /// </summary>
    public bool InitResult { get; set; } = true;

    /// <summary>Optional hook invoked during <see cref="RunCallbacks"/>.</summary>
    public Action? OnRunCallbacks { get; set; }

    /// <summary>Steam ID to return from <see cref="GetLocalSteamId"/>. Default: 0.</summary>
    public ulong FakeLocalSteamId { get; set; } = 0;

    /// <summary>AppID to return from <see cref="GetAppId"/>. Default: 480 (Spacewar).</summary>
    public uint FakeAppId { get; set; } = 480;

    /// <summary>HSteamPipe to return from <see cref="GetHSteamPipe"/>. Default: 0.</summary>
    public uint FakeHSteamPipe { get; set; } = 0;

    /// <inheritdoc/>
    public bool Init()
    {
        InitCalls++;
        if (InitException is not null)
            throw InitException;
        return InitResult;
    }

    /// <inheritdoc/>
    public void Shutdown() => ShutdownCalls++;

    /// <inheritdoc/>
    public void ManualDispatchInit() => ManualDispatchInitCalls++;

    /// <inheritdoc/>
    public void RunCallbacks()
    {
        RunCallbacksCalls++;
        OnRunCallbacks?.Invoke();
    }

    /// <inheritdoc/>
    public uint GetHSteamPipe() => FakeHSteamPipe;

    /// <inheritdoc/>
    public ulong GetLocalSteamId() => FakeLocalSteamId;

    /// <inheritdoc/>
    public uint GetAppId() => FakeAppId;
}
