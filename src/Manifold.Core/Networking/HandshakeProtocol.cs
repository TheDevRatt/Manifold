using System;
using System.Buffers.Binary;

namespace Manifold.Core.Networking;

/// <summary>
/// Encodes and decodes the Manifold peer-ID handshake protocol.
/// The handshake assigns a Godot peer ID to each connecting client.
/// (MASTER_DESIGN §8.7)
/// </summary>
/// <remarks>
/// Wire format:
/// <list type="bullet">
/// <item>Server→Client (Handshake): [PacketKind.Handshake][ch=0][peerId: int32 LE] — 6 bytes</item>
/// <item>Client→Server (Ack):       [PacketKind.HandshakeAck][ch=0]               — 2 bytes</item>
/// </list>
/// </remarks>
internal static class HandshakeProtocol
{
    /// <summary>Total size in bytes of the server→client handshake packet (header + peer ID).</summary>
    internal const int HandshakePacketSize = 6; // PacketHeader.Size(2) + sizeof(int)(4)

    /// <summary>Total size in bytes of the client→server acknowledgement packet.</summary>
    internal const int AckPacketSize = PacketHeader.Size; // 2

    /// <summary>
    /// Builds the server→client handshake packet for the given Godot peer ID.
    /// </summary>
    /// <param name="peerId">The Godot peer ID to assign. Must be ≥ 2.</param>
    internal static byte[] BuildHandshake(int peerId)
    {
        if (peerId < 2)
            throw new ArgumentOutOfRangeException(nameof(peerId),
                $"Godot peer ID must be ≥ 2 (1 is reserved for the server). Got: {peerId}.");
        var buf = new byte[HandshakePacketSize];
        new PacketHeader(PacketKind.Handshake, channel: 0).Encode(buf.AsSpan());
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(PacketHeader.Size), peerId);
        return buf;
    }

    /// <summary>
    /// Builds the client→server acknowledgement packet.
    /// </summary>
    internal static byte[] BuildAck()
    {
        var buf = new byte[AckPacketSize];
        new PacketHeader(PacketKind.HandshakeAck, channel: 0).Encode(buf.AsSpan());
        return buf;
    }

    /// <summary>
    /// Attempts to parse a server→client handshake packet and extract the assigned peer ID.
    /// </summary>
    /// <param name="data">The received packet data (must include the 2-byte header).</param>
    /// <param name="peerId">The extracted Godot peer ID on success.</param>
    /// <returns><c>true</c> if parsing succeeded; <c>false</c> if the data is malformed.</returns>
    internal static bool TryParseHandshake(ReadOnlySpan<byte> data, out int peerId)
    {
        peerId = 0;
        if (data.Length < HandshakePacketSize) return false;
        if (!PacketHeader.TryDecode(data, out var hdr)) return false;
        if (hdr.Kind != PacketKind.Handshake) return false;
        peerId = BinaryPrimitives.ReadInt32LittleEndian(data[PacketHeader.Size..]);
        return peerId >= 2; // peer IDs < 2 are malformed
    }

    /// <summary>
    /// Returns <c>true</c> if the given data is a valid client→server acknowledgement.
    /// </summary>
    internal static bool IsAck(ReadOnlySpan<byte> data)
    {
        if (!PacketHeader.TryDecode(data, out var hdr)) return false;
        return hdr.Kind == PacketKind.HandshakeAck;
    }
}

/// <summary>
/// Tracks the handshake state for a single connecting peer, including a configurable peer-connection timeout (default: 5 s).
/// (MASTER_DESIGN §8.7)
/// </summary>
internal sealed class HandshakeState
{
    private readonly long _deadlineTick;

    /// <summary><c>true</c> if the handshake completed successfully (ACK received).</summary>
    internal bool IsComplete { get; private set; }

    /// <summary>
    /// <c>true</c> if the handshake timeout has elapsed without completion.
    /// Uses <see cref="Environment.TickCount64"/> — monotonic, NTP-immune.
    /// </summary>
    internal bool IsExpired => !IsComplete && Environment.TickCount64 >= _deadlineTick;

    /// <summary>
    /// Initialises a new handshake state with the given (or default 5-second) timeout.
    /// </summary>
    /// <param name="timeoutMs">Override for the default 5000 ms timeout (useful in tests).</param>
    internal HandshakeState(long timeoutMs = 5_000)
        => _deadlineTick = Environment.TickCount64 + timeoutMs;

    /// <summary>Marks the handshake as successfully completed.</summary>
    internal void MarkComplete() => IsComplete = true;
}
