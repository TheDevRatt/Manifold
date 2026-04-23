// Manifold — SteamLifecycle contract tests

using System;
using Manifold.Core.Dispatch;
using Manifold.Core.Lifecycle;
using Xunit;

namespace Manifold.Core.Tests.Contract;

// SteamLifecycle has a process-wide singleton guard.
// Disable parallel execution within this test class.
[Collection("SteamLifecycle")]
public sealed class SteamLifecycleTests : IDisposable
{
    private SteamLifecycle? _lifecycle;

    public SteamLifecycleTests()
    {
        ResetStaticState();
    }

    public void Dispose()
    {
        _lifecycle?.Dispose();
        _lifecycle = null;
        ResetStaticState();
    }

    /// <summary>
    /// Resets the static _everInitialized flag and Current property between tests.
    /// Required because xUnit may reuse the process across tests in the same collection.
    /// </summary>
    private static void ResetStaticState()
    {
        var type = typeof(SteamLifecycle);

        var everField = type.GetField("_everInitialized",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        everField?.SetValue(null, false);

        var currentField = type.GetField("<Current>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        currentField?.SetValue(null, null);
    }

    // Helper: create a lifecycle and call Initialize on it, wiring up to _lifecycle.
    private SteamLifecycle CreateAndInitialize(
        FakeSteamInit? init = null,
        SteamInitOptions? opts = null)
    {
        init ??= new FakeSteamInit();
        opts ??= new SteamInitOptions();
        var dispatcher = new CallbackDispatcher();
        var lc         = new SteamLifecycle(init, dispatcher);
        var result     = lc.Initialize(opts);
        Assert.True(result.IsSuccess, result.Error);
        _lifecycle = lc;
        return lc;
    }

    // ── Initialize() success ──────────────────────────────────────────────────

    [Fact]
    public void Initialize_WithFakeInit_ReturnsOk()
    {
        var init       = new FakeSteamInit();
        var dispatcher = new CallbackDispatcher();
        var lc         = new SteamLifecycle(init, dispatcher);

        var result = lc.Initialize(new SteamInitOptions());

        Assert.True(result.IsSuccess);
        _lifecycle = lc;
    }

    [Fact]
    public void AfterInitialize_IsInitialized_IsTrue()
    {
        var lc = CreateAndInitialize();
        Assert.True(lc.IsInitialized);
    }

    [Fact]
    public void AfterInitialize_Current_IsNonNull()
    {
        var lc = CreateAndInitialize();
        Assert.NotNull(SteamLifecycle.Current);
        Assert.Same(lc, SteamLifecycle.Current);
    }

    [Fact]
    public void AfterDispose_Current_IsNull()
    {
        var lc = CreateAndInitialize();
        lc.Dispose();
        _lifecycle = null;

        Assert.Null(SteamLifecycle.Current);
    }

    // ── Init delegation ───────────────────────────────────────────────────────

    [Fact]
    public void Initialize_CallsInitOnce()
    {
        var init = new FakeSteamInit();
        CreateAndInitialize(init);
        Assert.Equal(1, init.InitCalls);
    }

    [Fact]
    public void Dispose_CallsShutdownOnce()
    {
        var init = new FakeSteamInit();
        var lc   = CreateAndInitialize(init);
        lc.Dispose();
        _lifecycle = null;

        Assert.Equal(1, init.ShutdownCalls);
    }

    [Fact]
    public void Initialize_WithManualDispatch_CallsManualDispatchInit()
    {
        var init = new FakeSteamInit();
        CreateAndInitialize(init, new SteamInitOptions { ManualDispatch = true });
        Assert.Equal(1, init.ManualDispatchInitCalls);
    }

    [Fact]
    public void Initialize_WithoutManualDispatch_DoesNotCallManualDispatchInit()
    {
        var init = new FakeSteamInit();
        CreateAndInitialize(init, new SteamInitOptions { ManualDispatch = false });
        Assert.Equal(0, init.ManualDispatchInitCalls);
    }

    // ── Init failure ──────────────────────────────────────────────────────────

    [Fact]
    public void WhenInitReturnsFalse_ReturnsFailResult()
    {
        var init       = new FakeSteamInit { InitResult = false };
        var dispatcher = new CallbackDispatcher();
        var lc         = new SteamLifecycle(init, dispatcher);

        var result = lc.Initialize(new SteamInitOptions());

        Assert.False(result.IsSuccess);
        Assert.Contains("SteamAPI_Init()", result.Error);
    }

    [Fact]
    public void WhenInitThrows_ReturnsFailResult()
    {
        var init = new FakeSteamInit
        {
            InitException = new SteamInitException("test failure")
        };
        var dispatcher = new CallbackDispatcher();
        var lc         = new SteamLifecycle(init, dispatcher);

        var result = lc.Initialize(new SteamInitOptions());

        Assert.False(result.IsSuccess);
    }

    // ── Singleton / once-per-process guard ────────────────────────────────────

    [Fact]
    public void SecondInitialize_WhileFirstActive_ReturnsFailWithAlreadyInitializedMessage()
    {
        var lc = CreateAndInitialize();

        var init2       = new FakeSteamInit();
        var dispatcher2 = new CallbackDispatcher();
        var lc2         = new SteamLifecycle(init2, dispatcher2);
        var result2     = lc2.Initialize(new SteamInitOptions());

        Assert.False(result2.IsSuccess);
        Assert.Equal("SteamLifecycle is already initialized.", result2.Error);
    }

    [Fact]
    public void InitializeAfterDispose_ReturnsFailWithDisposedMessage()
    {
        var lc = CreateAndInitialize();
        lc.Dispose();
        _lifecycle = null;

        var init2       = new FakeSteamInit();
        var dispatcher2 = new CallbackDispatcher();
        var lc2         = new SteamLifecycle(init2, dispatcher2);
        var result2     = lc2.Initialize(new SteamInitOptions());

        Assert.False(result2.IsSuccess);
        Assert.Contains("disposed", result2.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Idempotent Dispose ────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var init = new FakeSteamInit();
        var lc   = CreateAndInitialize(init);

        lc.Dispose();
        lc.Dispose(); // must not throw
        _lifecycle = null;

        Assert.Equal(1, init.ShutdownCalls); // Shutdown called exactly once
    }

    // ── RunCallbacks after Dispose ────────────────────────────────────────────

    [Fact]
    public void RunCallbacks_AfterDispose_IsSafeNoOp()
    {
        var lc = CreateAndInitialize();
        lc.Dispose();
        _lifecycle = null;

        // Must not throw
        lc.RunCallbacks();
    }

    // ── Events ────────────────────────────────────────────────────────────────

    [Fact]
    public void InitializedEvent_RaisedAfterInitialize()
    {
        bool raised    = false;
        var init       = new FakeSteamInit();
        var dispatcher = new CallbackDispatcher();
        var lc         = new SteamLifecycle(init, dispatcher);
        lc.Initialized += () => raised = true;

        var result = lc.Initialize(new SteamInitOptions());
        _lifecycle = lc;

        Assert.True(result.IsSuccess);
        Assert.True(raised);
    }

    [Fact]
    public void ShutdownEvent_RaisedAfterDispose()
    {
        bool raised = false;
        var lc      = CreateAndInitialize();
        lc.Shutdown += () => raised = true;
        lc.Dispose();
        _lifecycle = null;

        Assert.True(raised);
    }

    [Fact]
    public void FatalErrorEvent_RaisedWhenInitThrows()
    {
        Exception? captured = null;
        var init       = new FakeSteamInit { InitException = new InvalidOperationException("bang") };
        var dispatcher = new CallbackDispatcher();
        var lc         = new SteamLifecycle(init, dispatcher);
        lc.FatalError += ex => captured = ex;

        var result = lc.Initialize(new SteamInitOptions());

        Assert.False(result.IsSuccess);
        Assert.IsType<InvalidOperationException>(captured);
    }

    // ── Options stored ────────────────────────────────────────────────────────

    [Fact]
    public void Options_StoredAfterInitialize()
    {
        var opts = new SteamInitOptions { ManualDispatch = false, CallResultTimeout = TimeSpan.FromSeconds(10) };
        var lc   = CreateAndInitialize(opts: opts);

        Assert.Equal(opts, lc.Options);
    }

    // ── AppId / LocalUser populated ───────────────────────────────────────────

    [Fact]
    public void AppId_PopulatedFromInit()
    {
        var init = new FakeSteamInit { FakeAppId = 12345 };
        var lc   = CreateAndInitialize(init);

        Assert.Equal(12345u, lc.AppId);
    }

    [Fact]
    public void LocalUser_PopulatedFromInit()
    {
        var init = new FakeSteamInit { FakeLocalSteamId = 76561198012345678 };
        var lc   = CreateAndInitialize(init);

        Assert.Equal(76561198012345678UL, lc.LocalUser.Value);
    }
}
