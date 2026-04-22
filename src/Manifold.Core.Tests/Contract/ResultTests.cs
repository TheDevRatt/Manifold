// Manifold.Core.Tests — Contract tests for Result<T>

using Manifold.Core;
using Xunit;

namespace Manifold.Core.Tests.Contract;

public sealed class ResultTests
{
    // ── Ok ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Ok_IsSuccess_IsTrue()
    {
        var r = Result<int>.Ok(42);
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public void Ok_Value_IsSet()
    {
        var r = Result<string>.Ok("hello");
        Assert.Equal("hello", r.Value);
    }

    [Fact]
    public void Ok_Error_IsEmpty()
    {
        var r = Result<int>.Ok(1);
        Assert.Equal(string.Empty, r.Error);
    }

    [Fact]
    public void Ok_TryGetValue_ReturnsTrueAndValue()
    {
        var r = Result<int>.Ok(99);
        Assert.True(r.TryGetValue(out int value, out string error));
        Assert.Equal(99, value);
        Assert.Equal(string.Empty, error);
    }

    // ── Fail ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Fail_IsSuccess_IsFalse()
    {
        var r = Result<int>.Fail("something went wrong");
        Assert.False(r.IsSuccess);
    }

    [Fact]
    public void Fail_Error_IsSet()
    {
        var r = Result<int>.Fail("bad state");
        Assert.Equal("bad state", r.Error);
    }

    [Fact]
    public void Fail_Value_IsDefault()
    {
        var r = Result<int>.Fail("oops");
        Assert.Equal(default, r.Value);
    }

    [Fact]
    public void Fail_TryGetValue_ReturnsFalseAndError()
    {
        var r = Result<string>.Fail("no connection");
        Assert.False(r.TryGetValue(out string value, out string error));
        Assert.Equal(default!, value);  // caller must not use this
        Assert.Equal("no connection", error);
    }

    // ── ToString ─────────────────────────────────────────────────────────────

    [Fact]
    public void Ok_ToString_ContainsValue()
    {
        var r = Result<int>.Ok(7);
        Assert.Contains("7", r.ToString());
    }

    [Fact]
    public void Fail_ToString_ContainsError()
    {
        var r = Result<int>.Fail("boom");
        Assert.Contains("boom", r.ToString());
    }
}
