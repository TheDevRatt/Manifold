using System;

namespace Manifold.Core.Networking;

/// <summary>
/// Encodes and decodes the 2-byte Manifold internal packet header.
/// Every packet sent or received by SteamMultiplayerPeer carries this header,
/// transparent to the Godot MultiplayerAPI.
/// (MASTER_DESIGN §8.5)
/// </summary>
/// <remarks>
/// <code>
/// Byte 0 — Version + Kind:
///   Upper nibble [7:4] — Protocol version (currently always 0x0)
///   Lower nibble [3:0] — Packet kind (see <see cref="PacketKind"/>)
/// Byte 1 — Channel Index (0–255)
/// </code>
/// </remarks>
internal readonly struct PacketHeader
{
    /// <summary>Fixed size of the header in bytes.</summary>
    public const int Size = 2;

    /// <summary>Protocol version. Currently always 0; reserved for future format changes.</summary>
    public byte Version { get; }

    /// <summary>The packet kind (lower nibble of byte 0).</summary>
    public PacketKind Kind { get; }

    /// <summary>Channel index (byte 1). Emulates ENet-style channels over a single Steam connection.</summary>
    public byte Channel { get; }

    /// <summary>Initialises a packet header.</summary>
    /// <param name="kind">The packet kind.</param>
    /// <param name="channel">Channel index (0–255).</param>
    /// <param name="version">Protocol version. Must be 0 in the current protocol.</param>
    public PacketHeader(PacketKind kind, byte channel, byte version = 0)
    {
        if (version > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(version),
                $"Version must be 0–15 (nibble range). Got: {version}.");
        Version = version;
        Kind    = kind;
        Channel = channel;
    }

    /// <summary>
    /// Encodes this header into the first 2 bytes of <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">The buffer to write into. Must be at least <see cref="Size"/> bytes.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="destination"/> is shorter than 2 bytes.</exception>
    public void Encode(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Buffer must be at least {Size} bytes.", nameof(destination));

        destination[0] = (byte)((Version << 4) | ((byte)Kind & 0x0F));
        destination[1] = Channel;
    }

    /// <summary>
    /// Attempts to decode a header from <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Source buffer; must be at least 2 bytes.</param>
    /// <param name="header">The decoded header on success; default on failure.</param>
    /// <returns><c>true</c> if successful; <c>false</c> if the buffer is too short.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> source, out PacketHeader header)
    {
        if (source.Length < Size)
        {
            header = default;
            return false;
        }

        byte version = (byte)(source[0] >> 4);
        var  kind    = (PacketKind)(source[0] & 0x0F);
        byte channel = source[1];
        header = new PacketHeader(kind, channel, version);
        return true;
    }
}

/// <summary>
/// Packet kinds encoded in the lower nibble of PacketHeader byte 0.
/// (MASTER_DESIGN §8.5)
/// </summary>
internal enum PacketKind : byte
{
    /// <summary>Normal RPC/game data.</summary>
    Data         = 0x0,
    /// <summary>Server→client: peer ID assignment.</summary>
    Handshake    = 0x1,
    /// <summary>Client→server: handshake acknowledgement.</summary>
    HandshakeAck = 0x2,
    /// <summary>Graceful close with reason code.</summary>
    Disconnect   = 0x3,
    // 0x4–0xF: reserved
}
