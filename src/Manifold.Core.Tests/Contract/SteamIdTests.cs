// Manifold.Core.Tests — Contract tests for SteamId

using Manifold.Core;
using Xunit;

namespace Manifold.Core.Tests.Contract;

public sealed class SteamIdTests
{
    private const ulong ValidRaw  = 76561198000000001UL;
    private const ulong ValidRaw2 = 76561198000000002UL;

    // ── Construction & validity ───────────────────────────────────────────────

    [Fact]
    public void Invalid_Value_IsZero()
        => Assert.Equal(0UL, SteamId.Invalid.Value);

    [Fact]
    public void Invalid_IsValid_IsFalse()
        => Assert.False(SteamId.Invalid.IsValid);

    [Fact]
    public void NonZero_IsValid_IsTrue()
        => Assert.True(new SteamId(ValidRaw).IsValid);

    // ── Equality (record struct) ──────────────────────────────────────────────

    [Fact]
    public void SameValue_AreEqual()
        => Assert.Equal(new SteamId(ValidRaw), new SteamId(ValidRaw));

    [Fact]
    public void DifferentValue_AreNotEqual()
        => Assert.NotEqual(new SteamId(ValidRaw), new SteamId(ValidRaw2));

    // ── Comparison ───────────────────────────────────────────────────────────

    [Fact]
    public void CompareTo_LessThan_IsNegative()
        => Assert.True(new SteamId(1).CompareTo(new SteamId(2)) < 0);

    [Fact]
    public void CompareTo_GreaterThan_IsPositive()
        => Assert.True(new SteamId(2).CompareTo(new SteamId(1)) > 0);

    [Fact]
    public void CompareTo_Equal_IsZero()
        => Assert.Equal(0, new SteamId(1).CompareTo(new SteamId(1)));

    [Fact]
    public void LessThanOperator_Works()
        => Assert.True(new SteamId(1) < new SteamId(2));

    [Fact]
    public void GreaterThanOperator_Works()
        => Assert.True(new SteamId(2) > new SteamId(1));

    [Fact]
    public void LessThanOrEqualOperator_EqualValues()
        => Assert.True(new SteamId(1) <= new SteamId(1));

    [Fact]
    public void GreaterThanOrEqualOperator_EqualValues()
        => Assert.True(new SteamId(1) >= new SteamId(1));

    // ── Conversion ───────────────────────────────────────────────────────────

    [Fact]
    public void ImplicitToUlong_ReturnsValue()
    {
        SteamId id = new(ValidRaw);
        ulong raw  = id;
        Assert.Equal(ValidRaw, raw);
    }

    [Fact]
    public void ExplicitFromUlong_WrapsValue()
    {
        SteamId id = (SteamId)ValidRaw;
        Assert.Equal(ValidRaw, id.Value);
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidDecimalString_ReturnsCorrectId()
    {
        var id = SteamId.Parse(ValidRaw.ToString());
        Assert.Equal(ValidRaw, id.Value);
    }

    [Fact]
    public void Parse_InvalidString_ThrowsFormatException()
        => Assert.Throws<FormatException>(() => SteamId.Parse("not-a-number"));

    [Fact]
    public void TryParse_ValidString_ReturnsTrueAndId()
    {
        bool ok = SteamId.TryParse(ValidRaw.ToString(), out SteamId id);
        Assert.True(ok);
        Assert.Equal(ValidRaw, id.Value);
    }

    [Fact]
    public void TryParse_InvalidString_ReturnsFalseAndInvalid()
    {
        bool ok = SteamId.TryParse("garbage", out SteamId id);
        Assert.False(ok);
        Assert.Equal(SteamId.Invalid, id);
    }

    [Fact]
    public void TryParse_Null_ReturnsFalseAndInvalid()
    {
        bool ok = SteamId.TryParse(null, out SteamId id);
        Assert.False(ok);
        Assert.Equal(SteamId.Invalid, id);
    }

    // ── ToString ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_MatchesRawValue()
        => Assert.Equal(ValidRaw.ToString(), new SteamId(ValidRaw).ToString());

    // ── JSON round-trip ──────────────────────────────────────────────────────

    [Fact]
    public void JsonRoundTrip_PreservesValue()
    {
        var id      = new SteamId(ValidRaw);
        string json = System.Text.Json.JsonSerializer.Serialize(id);
        var back    = System.Text.Json.JsonSerializer.Deserialize<SteamId>(json);
        Assert.Equal(id, back);
    }

    [Fact]
    public void JsonSerialises_AsString_NotNumber()
    {
        var id      = new SteamId(ValidRaw);
        string json = System.Text.Json.JsonSerializer.Serialize(id);
        // Must be a JSON string (quoted) to avoid JS 64-bit precision loss
        Assert.StartsWith("\"", json);
        Assert.EndsWith("\"", json);
    }
}
