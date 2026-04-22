// Manifold — SteamLifecycle contract tests

using System;
using System.Threading;
using Manifold.Core.Dispatch;
using Manifold.Core.Lifecycle;
using Xunit;

namespace Manifold.Core.Tests.Contract;

// SteamLifecycle has a process-wide singleton guard.
// Disable parallel execution within this test class.
[Collection("SteamLifecycle")]
public sealed class SteamLifecycleTests : IDisposable
{
    private readonly FakeSteamInit _init = new();
    private readonly CallbackDispatcher _dispatcher = new();
    private readonly SteamLifecycle _lifecycle;

    public SteamLifecycleTests()
    {
        _lifecycle = new SteamLifecycle(_init, _dispatcher);
    }

    public void Dispose() => _lifecycle.Dispose();

    // ── State transitions ─────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsUninitialized()
    {
        Assert.Equal(SteamLifecycle.State.Uninitialized, _lifecycle.CurrentState);
    }

    [Fact]
    public void AfterStart_StateIsRunning()
    {
        _lifecycle.Start();
        Assert.Equal(SteamLifecycle.State.Running, _lifecycle.CurrentState);
    }

    [Fact]
    public void AfterStop_StateIsStopped()
    {
        _lifecycle.Start();
        _lifecycle.Stop();
        Assert.Equal(SteamLifecycle.State.Stopped, _lifecycle.CurrentState);
    }

    [Fact]
    public void DoubleStop_IsIdempotent()
    {
        _lifecycle.Start();
        _lifecycle.Stop();
        _lifecycle.Stop(); // should not throw
        Assert.Equal(SteamLifecycle.State.Stopped, _lifecycle.CurrentState);
    }

    // ── Init delegation ───────────────────────────────────────────────────────

    [Fact]
    public void Start_CallsInitOnce()
    {
        _lifecycle.Start();
        Assert.Equal(1, _init.InitCalls);
    }

    [Fact]
    public void Stop_CallsShutdownOnce()
    {
        _lifecycle.Start();
        _lifecycle.Stop();
        Assert.Equal(1, _init.ShutdownCalls);
    }

    [Fact]
    public void PumpThread_CallsRunCallbacks()
    {
        var called = new ManualResetEventSlim(false);
        _init.OnRunCallbacks = () => called.Set();

        _lifecycle.Start(pumpInterval: TimeSpan.FromMilliseconds(10));

        bool fired = called.Wait(TimeSpan.FromSeconds(2));
        _lifecycle.Stop();

        Assert.True(fired, "RunCallbacks was never called by the pump thread.");
    }

    // ── Init failure ──────────────────────────────────────────────────────────

    [Fact]
    public void WhenInitFails_ThrowsSteamInitException()
    {
        _init.InitException = new SteamInitException("test failure");
        Assert.Throws<SteamInitException>(() => _lifecycle.Start());
    }

    [Fact]
    public void WhenInitFails_StateIsStopped()
    {
        _init.InitException = new SteamInitException("test failure");
        try { _lifecycle.Start(); } catch { /* expected */ }
        Assert.Equal(SteamLifecycle.State.Stopped, _lifecycle.CurrentState);
    }

    // ── Events ────────────────────────────────────────────────────────────────

    [Fact]
    public void InitializedEvent_RaisedAfterStart()
    {
        bool raised = false;
        _lifecycle.Initialized += () => raised = true;
        _lifecycle.Start();
        Assert.True(raised);
    }

    [Fact]
    public void ShutdownEvent_RaisedAfterStop()
    {
        bool raised = false;
        _lifecycle.Shutdown += () => raised = true;
        _lifecycle.Start();
        _lifecycle.Stop();
        Assert.True(raised);
    }

    [Fact]
    public void FatalErrorEvent_RaisedWhenPumpThrows()
    {
        Exception? captured = null;
        var errEvent = new ManualResetEventSlim(false);

        _lifecycle.FatalError += ex =>
        {
            captured = ex;
            errEvent.Set();
        };

        var boom = false;
        _init.OnRunCallbacks = () =>
        {
            if (!boom) { boom = true; throw new InvalidOperationException("pump exploded"); }
        };

        _lifecycle.Start(pumpInterval: TimeSpan.FromMilliseconds(10));
        bool fired = errEvent.Wait(TimeSpan.FromSeconds(2));

        // Give the pump thread a moment to finish writing Stopped state
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_lifecycle.CurrentState != SteamLifecycle.State.Stopped
               && DateTime.UtcNow < deadline)
            Thread.Sleep(5);

        Assert.True(fired, "FatalError event was not raised.");
        Assert.IsType<InvalidOperationException>(captured);
        Assert.Equal(SteamLifecycle.State.Stopped, _lifecycle.CurrentState);
    }

    // ── Singleton guard ───────────────────────────────────────────────────────

    [Fact]
    public void TwoInstances_CannotBothStart()
    {
        using var second = new SteamLifecycle(new FakeSteamInit(), new CallbackDispatcher());

        _lifecycle.Start();
        Assert.Throws<InvalidOperationException>(() => second.Start());

        _lifecycle.Stop();
    }

    [Fact]
    public void AfterFirstStops_SecondCanStart()
    {
        using var second = new SteamLifecycle(new FakeSteamInit(), new CallbackDispatcher());

        _lifecycle.Start();
        _lifecycle.Stop();

        second.Start(); // must not throw
        Assert.Equal(SteamLifecycle.State.Running, second.CurrentState);
        second.Stop();
    }
}
