using System;
using System.Threading;
using Manifold.Core.Networking;
using Xunit;

namespace Manifold.Core.Tests.Protocol;

public class HandshakeProtocolTests
{
    // ─── BuildHandshake ────────────────────────────────────────────────────────

    [Fact]
    public void BuildHandshake_PeerId2_ProducesCorrectBytes()
    {
        // header: [0x01, 0x00], peerId=2 as int32 LE: [0x02, 0x00, 0x00, 0x00]
        var bytes = HandshakeProtocol.BuildHandshake(2);

        Assert.Equal(HandshakeProtocol.HandshakePacketSize, bytes.Length);
        Assert.Equal(0x01, bytes[0]); // PacketKind.Handshake, version=0
        Assert.Equal(0x00, bytes[1]); // channel=0
        Assert.Equal(0x02, bytes[2]); // int32 LE: 2
        Assert.Equal(0x00, bytes[3]);
        Assert.Equal(0x00, bytes[4]);
        Assert.Equal(0x00, bytes[5]);
    }

    [Fact]
    public void BuildHandshake_LargePeerId_ProducesCorrectBytes()
    {
        // peerId=256 → int32 LE: [0x00, 0x01, 0x00, 0x00]
        var bytes = HandshakeProtocol.BuildHandshake(256);

        Assert.Equal(HandshakeProtocol.HandshakePacketSize, bytes.Length);
        Assert.Equal(0x01, bytes[0]);
        Assert.Equal(0x00, bytes[1]);
        Assert.Equal(0x00, bytes[2]); // int32 LE: 256
        Assert.Equal(0x01, bytes[3]);
        Assert.Equal(0x00, bytes[4]);
        Assert.Equal(0x00, bytes[5]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildHandshake_Throws_WhenPeerIdIsLessThan2(int invalidId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HandshakeProtocol.BuildHandshake(invalidId));
    }

    // ─── BuildAck ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildAck_ProducesTwoBytePacket()
    {
        var bytes = HandshakeProtocol.BuildAck();

        Assert.Equal(HandshakeProtocol.AckPacketSize, bytes.Length);
        Assert.Equal(0x02, bytes[0]); // PacketKind.HandshakeAck, version=0
        Assert.Equal(0x00, bytes[1]); // channel=0
    }

    // ─── TryParseHandshake ────────────────────────────────────────────────────

    [Fact]
    public void TryParseHandshake_ValidPacket_ReturnsTrueWithCorrectPeerId()
    {
        var packet = HandshakeProtocol.BuildHandshake(42);
        var ok = HandshakeProtocol.TryParseHandshake(packet, out int peerId);

        Assert.True(ok);
        Assert.Equal(42, peerId);
    }

    [Fact]
    public void TryParseHandshake_TooShort_ReturnsFalse()
    {
        ReadOnlySpan<byte> data = stackalloc byte[] { 0x01 };
        var ok = HandshakeProtocol.TryParseHandshake(data, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryParseHandshake_WrongKind_ReturnsFalse()
    {
        // Build a Data packet (kind=0x0) — not a Handshake
        var header = new PacketHeader(PacketKind.Data, channel: 0);
        var buf = new byte[HandshakeProtocol.HandshakePacketSize];
        header.Encode(buf.AsSpan());
        // fill peer id bytes with 2 so length check passes
        buf[2] = 0x02;

        var ok = HandshakeProtocol.TryParseHandshake(buf, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryParseHandshake_PeerIdLessThan2_ReturnsFalse()
    {
        // Manually build a Handshake packet with peerId = 1 (malformed)
        var buf = new byte[HandshakeProtocol.HandshakePacketSize];
        new PacketHeader(PacketKind.Handshake, channel: 0).Encode(buf.AsSpan());
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            buf.AsSpan(PacketHeader.Size), 1);

        var ok = HandshakeProtocol.TryParseHandshake(buf, out _);
        Assert.False(ok);
    }

    // ─── IsAck ────────────────────────────────────────────────────────────────

    [Fact]
    public void IsAck_ValidAckPacket_ReturnsTrue()
    {
        var ack = HandshakeProtocol.BuildAck();
        Assert.True(HandshakeProtocol.IsAck(ack));
    }

    [Fact]
    public void IsAck_HandshakePacket_ReturnsFalse()
    {
        var handshake = HandshakeProtocol.BuildHandshake(2);
        Assert.False(HandshakeProtocol.IsAck(handshake));
    }

    [Fact]
    public void IsAck_EmptyBuffer_ReturnsFalse()
    {
        Assert.False(HandshakeProtocol.IsAck(ReadOnlySpan<byte>.Empty));
    }

    // ─── Round-trip ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(255)]
    [InlineData(65536)]
    public void BuildHandshake_ThenTryParse_RoundTrips(int originalPeerId)
    {
        var packet = HandshakeProtocol.BuildHandshake(originalPeerId);
        var ok = HandshakeProtocol.TryParseHandshake(packet, out int parsedPeerId);

        Assert.True(ok);
        Assert.Equal(originalPeerId, parsedPeerId);
    }

    // ─── TryParseHandshakePayload (post-header payload only) ──────────────────

    [Theory]
    [InlineData(2)]
    [InlineData(42)]
    [InlineData(255)]
    [InlineData(65536)]
    public void TryParseHandshakePayload_ValidPayload_RoundTrips(int originalPeerId)
    {
        // Build a full handshake, then strip the 2-byte header to simulate
        // what ProcessMessage delivers to _Poll().
        var fullPacket = HandshakeProtocol.BuildHandshake(originalPeerId);
        var payload = fullPacket.AsSpan(PacketHeader.Size);

        var ok = HandshakeProtocol.TryParseHandshakePayload(payload, out int parsedId);
        Assert.True(ok);
        Assert.Equal(originalPeerId, parsedId);
    }

    [Fact]
    public void TryParseHandshakePayload_TooShort_ReturnsFalse()
    {
        ReadOnlySpan<byte> data = stackalloc byte[] { 0x02, 0x00, 0x00 }; // 3 bytes, need 4
        Assert.False(HandshakeProtocol.TryParseHandshakePayload(data, out _));
    }

    [Fact]
    public void TryParseHandshakePayload_PeerIdLessThan2_ReturnsFalse()
    {
        // peerId=1 as int32 LE
        ReadOnlySpan<byte> data = stackalloc byte[] { 0x01, 0x00, 0x00, 0x00 };
        Assert.False(HandshakeProtocol.TryParseHandshakePayload(data, out _));
    }

    [Fact]
    public void TryParseHandshakePayload_Empty_ReturnsFalse()
    {
        Assert.False(HandshakeProtocol.TryParseHandshakePayload(ReadOnlySpan<byte>.Empty, out _));
    }

    // ─── Sad-path / duplicate ACK tests ──────────────────────────────────────

    [Fact]
    public void TryParseHandshake_WithAckBytes_ReturnsFalse()
    {
        // An ACK packet (HandshakeAck kind) must not be parsed as a Handshake
        var ack = HandshakeProtocol.BuildAck();
        bool result = HandshakeProtocol.TryParseHandshake(ack, out _);
        Assert.False(result); // kind=HandshakeAck != Handshake
    }

    [Fact]
    public void IsAck_WithHandshakeAck_ThenSamePacket_SecondCallStillReturnsTrue()
    {
        // Simulates duplicate ACK — IsAck must be stateless and idempotent
        var ack = HandshakeProtocol.BuildAck();
        Assert.True(HandshakeProtocol.IsAck(ack));
        Assert.True(HandshakeProtocol.IsAck(ack)); // duplicate — no state change expected
    }

    [Fact]
    public void TryParseHandshake_DataPacket_ReturnsFalse()
    {
        // A data packet must not be parsed as a handshake
        Span<byte> buf = stackalloc byte[PacketHeader.Size + sizeof(int)];
        new PacketHeader(PacketKind.Data, channel: 0).Encode(buf);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf[PacketHeader.Size..], 42);
        Assert.False(HandshakeProtocol.TryParseHandshake(buf, out _));
    }
}

public class HandshakeStateTests
{
    [Fact]
    public void HandshakeState_IsNotComplete_Initially()
    {
        var state = new HandshakeState();
        Assert.False(state.IsComplete);
    }

    [Fact]
    public void HandshakeState_IsNotExpired_WithinTimeout()
    {
        var state = new HandshakeState(timeoutMs: 5_000);
        Assert.False(state.IsExpired);
    }

    [Fact]
    public void HandshakeState_IsExpired_AfterTimeout()
    {
        var state = new HandshakeState(timeoutMs: 1);
        Thread.Sleep(10);
        Assert.True(state.IsExpired);
    }

    [Fact]
    public void HandshakeState_MarkComplete_PreventsExpiry()
    {
        var state = new HandshakeState(timeoutMs: 1);
        state.MarkComplete();
        Thread.Sleep(10); // deadline passes
        Assert.False(state.IsExpired); // complete → never expired
    }

    [Fact]
    public void HandshakeState_IsComplete_AfterMarkComplete()
    {
        var state = new HandshakeState();
        state.MarkComplete();
        Assert.True(state.IsComplete);
    }
}
