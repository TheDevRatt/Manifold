// Manifold — SteamId
// Strongly-typed wrapper for Steam's 64-bit user/lobby/group identifiers.
// Replaces raw ulong to prevent ID confusion and adds rich domain behaviour.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Manifold.Core;

/// <summary>
/// A strongly-typed, immutable wrapper for a Steam 64-bit ID.
/// Represents a Steam user, lobby, game server, or group identity.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("{DebugDisplay,nq}")]
[JsonConverter(typeof(SteamIdJsonConverter))]
public readonly record struct SteamId : IEquatable<SteamId>, IComparable<SteamId>
{
    /// <summary>The raw 64-bit Steam ID value.</summary>
    public readonly ulong Value;

    /// <summary>Initialises a <see cref="SteamId"/> with the given raw value.</summary>
    public SteamId(ulong value) => Value = value;

    /// <summary>Represents an invalid or unset Steam ID (value 0).</summary>
    public static readonly SteamId Invalid = new(0);

    /// <summary><c>true</c> if this ID is non-zero and may represent a real Steam entity.</summary>
    public bool IsValid => Value != 0;

    // ── Comparison ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public int CompareTo(SteamId other) => Value.CompareTo(other.Value);

    /// <inheritdoc/>
    public static bool operator <(SteamId a, SteamId b)  => a.Value < b.Value;
    /// <inheritdoc/>
    public static bool operator >(SteamId a, SteamId b)  => a.Value > b.Value;
    /// <inheritdoc/>
    public static bool operator <=(SteamId a, SteamId b) => a.Value <= b.Value;
    /// <inheritdoc/>
    public static bool operator >=(SteamId a, SteamId b) => a.Value >= b.Value;

    // ── Conversion ───────────────────────────────────────────────────────────

    /// <summary>Implicitly converts a <see cref="SteamId"/> to its underlying <see cref="ulong"/> value.</summary>
    public static implicit operator ulong(SteamId id)     => id.Value;

    /// <summary>
    /// Explicitly converts a raw <see cref="ulong"/> to a <see cref="SteamId"/>.
    /// Use <see cref="Parse"/> or <see cref="TryParse"/> when converting from strings.
    /// </summary>
    public static explicit operator SteamId(ulong value)  => new(value);

    // ── Parsing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a decimal string representation of a Steam ID.
    /// </summary>
    /// <exception cref="FormatException">Thrown if <paramref name="s"/> is not a valid ulong.</exception>
    public static SteamId Parse(string s)
        => new(ulong.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture));

    /// <summary>
    /// Attempts to parse a decimal string representation of a Steam ID.
    /// </summary>
    /// <returns><c>true</c> if parsing succeeded; <c>false</c> otherwise.</returns>
    public static bool TryParse(string? s, out SteamId result)
    {
        if (ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out ulong v))
        {
            result = new(v);
            return true;
        }
        result = Invalid;
        return false;
    }

    // ── Display ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    private string DebugDisplay => IsValid ? $"SteamId({Value})" : "SteamId(Invalid)";
}

/// <summary>
/// JSON converter for <see cref="SteamId"/>.
/// Serialises as a decimal string to avoid JavaScript 64-bit number precision loss.
/// </summary>
public sealed class SteamIdJsonConverter : JsonConverter<SteamId>
{
    /// <inheritdoc/>
    public override SteamId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            return SteamId.TryParse(s, out var id) ? id : SteamId.Invalid;
        }
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetUInt64(out ulong raw))
            return new SteamId(raw);

        return SteamId.Invalid;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, SteamId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
