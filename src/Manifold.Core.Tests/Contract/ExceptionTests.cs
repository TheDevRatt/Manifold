// Manifold.Core.Tests — Contract tests for exception hierarchy

using Manifold.Core;
using Xunit;

namespace Manifold.Core.Tests.Contract;

public sealed class ExceptionTests
{
    // ── SteamInitException ────────────────────────────────────────────────────

    [Fact]
    public void SteamInitException_IsSteamException()
        => Assert.IsAssignableFrom<SteamException>(new SteamInitException("fail"));

    [Fact]
    public void SteamInitException_StoresResultCode()
        => Assert.Equal(42, new SteamInitException("fail", 42).ResultCode);

    // ── SteamIOFailedException ────────────────────────────────────────────────

    [Fact]
    public void SteamIOFailedException_IsSteamException()
        => Assert.IsAssignableFrom<SteamException>(new SteamIOFailedException(1, "CreateLobby"));

    [Fact]
    public void SteamIOFailedException_StoresApiCall()
        => Assert.Equal(999UL, new SteamIOFailedException(999, "op").ApiCall);

    [Fact]
    public void SteamIOFailedException_StoresOperationHint()
        => Assert.Equal("CreateLobby", new SteamIOFailedException(1, "CreateLobby").OperationHint);

    [Fact]
    public void SteamIOFailedException_NoBestEffortResult_IsNull()
        => Assert.Null(new SteamIOFailedException(1, "op").BestEffortResult);

    [Fact]
    public void SteamIOFailedException_WithBestEffortResult_IsSet()
        => Assert.Equal(11, new SteamIOFailedException(1, "op", 11).BestEffortResult);

    [Fact]
    public void SteamIOFailedException_Message_ContainsHint()
        => Assert.Contains("CreateLobby", new SteamIOFailedException(1, "CreateLobby").Message);

    // ── SteamCallResultTimeoutException ──────────────────────────────────────

    [Fact]
    public void SteamCallResultTimeoutException_IsSteamException()
        => Assert.IsAssignableFrom<SteamException>(
            new SteamCallResultTimeoutException(1, TimeSpan.FromSeconds(30)));

    // ── SteamShutdownException ────────────────────────────────────────────────

    [Fact]
    public void SteamShutdownException_IsSteamException()
        => Assert.IsAssignableFrom<SteamException>(new SteamShutdownException());

    [Fact]
    public void SteamShutdownException_HasMessage()
        => Assert.False(string.IsNullOrWhiteSpace(new SteamShutdownException().Message));

    // ── SteamDisposedException ────────────────────────────────────────────────

    [Fact]
    public void SteamDisposedException_IsSteamException()
        => Assert.IsAssignableFrom<SteamException>(new SteamDisposedException("RunCallbacks"));

    [Fact]
    public void SteamDisposedException_Message_ContainsMemberName()
        => Assert.Contains("RunCallbacks", new SteamDisposedException("RunCallbacks").Message);

    // ── SteamException base ───────────────────────────────────────────────────

    [Fact]
    public void AllExceptions_DeriveFromSteamException()
    {
        Assert.IsAssignableFrom<SteamException>(new SteamInitException("x"));
        Assert.IsAssignableFrom<SteamException>(new SteamIOFailedException(0, "x"));
        Assert.IsAssignableFrom<SteamException>(new SteamCallResultTimeoutException(0, TimeSpan.Zero));
        Assert.IsAssignableFrom<SteamException>(new SteamShutdownException());
        Assert.IsAssignableFrom<SteamException>(new SteamDisposedException("x"));
    }
}
