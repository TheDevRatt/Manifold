using System;
using Manifold.Core.Networking;
using Xunit;

namespace Manifold.Core.Tests.Protocol;

public class PacketHeaderTests
{
    // ─── Encode ────────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_Data_OnChannel0_ProducesCorrectBytes()
    {
        // version=0, kind=Data(0x0), channel=0  →  [0x00, 0x00]
        var header = new PacketHeader(PacketKind.Data, channel: 0);
        Span<byte> buf = stackalloc byte[2];
        header.Encode(buf);

        Assert.Equal(0x00, buf[0]);
        Assert.Equal(0x00, buf[1]);
    }

    [Fact]
    public void Encode_Handshake_OnChannel5_ProducesCorrectBytes()
    {
        // version=0, kind=Handshake(0x1), channel=5  →  [0x01, 0x05]
        var header = new PacketHeader(PacketKind.Handshake, channel: 5);
        Span<byte> buf = stackalloc byte[2];
        header.Encode(buf);

        Assert.Equal(0x01, buf[0]);
        Assert.Equal(0x05, buf[1]);
    }

    [Fact]
    public void Encode_HandshakeAck_ProducesCorrectBytes()
    {
        // version=0, kind=HandshakeAck(0x2), channel=0  →  [0x02, 0x00]
        var header = new PacketHeader(PacketKind.HandshakeAck, channel: 0);
        Span<byte> buf = stackalloc byte[2];
        header.Encode(buf);

        Assert.Equal(0x02, buf[0]);
        Assert.Equal(0x00, buf[1]);
    }

    [Fact]
    public void Encode_Disconnect_OnChannel255_ProducesCorrectBytes()
    {
        // version=0, kind=Disconnect(0x3), channel=255  →  [0x03, 0xFF]
        var header = new PacketHeader(PacketKind.Disconnect, channel: 255);
        Span<byte> buf = stackalloc byte[2];
        header.Encode(buf);

        Assert.Equal(0x03, buf[0]);
        Assert.Equal(0xFF, buf[1]);
    }

    [Fact]
    public void Encode_WithNonZeroVersion_EncodesVersionInUpperNibble()
    {
        // version=1, kind=Handshake(0x1), channel=0  →  byte0 = 0x11
        var header = new PacketHeader(PacketKind.Handshake, channel: 0, version: 1);
        Span<byte> buf = stackalloc byte[2];
        header.Encode(buf);

        Assert.Equal(0x11, buf[0]);
        Assert.Equal(0x00, buf[1]);
    }

    // ─── TryDecode ─────────────────────────────────────────────────────────────

    [Fact]
    public void TryDecode_ValidBuffer_ReturnsTrue_And_CorrectFields()
    {
        // [0x01, 0x05]  →  version=0, kind=Handshake, channel=5
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x01, 0x05 };
        bool ok = PacketHeader.TryDecode(buf, out var header);

        Assert.True(ok);
        Assert.Equal(0, header.Version);
        Assert.Equal(PacketKind.Handshake, header.Kind);
        Assert.Equal(5, header.Channel);
    }

    [Fact]
    public void TryDecode_EmptyBuffer_ReturnsFalse()
    {
        bool ok = PacketHeader.TryDecode(ReadOnlySpan<byte>.Empty, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryDecode_OneByteBuffer_ReturnsFalse()
    {
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x01 };
        bool ok = PacketHeader.TryDecode(buf, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryDecode_ExactlyTwoBytes_ReturnsTrueAndDecodes()
    {
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x03, 0x7F };
        bool ok = PacketHeader.TryDecode(buf, out var header);

        Assert.True(ok);
        Assert.Equal(PacketKind.Disconnect, header.Kind);
        Assert.Equal(0x7F, header.Channel);
    }

    [Fact]
    public void TryDecode_LargerBuffer_OnlyConsumesFirstTwoBytes()
    {
        // [0x02, 0x07, 0xFF, 0xAB]  →  HandshakeAck, channel=7; rest ignored
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x02, 0x07, 0xFF, 0xAB };
        bool ok = PacketHeader.TryDecode(buf, out var header);

        Assert.True(ok);
        Assert.Equal(PacketKind.HandshakeAck, header.Kind);
        Assert.Equal(0x07, header.Channel);
    }

    // ─── Round-trips ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData((int)PacketKind.Data)]
    [InlineData((int)PacketKind.Handshake)]
    [InlineData((int)PacketKind.HandshakeAck)]
    [InlineData((int)PacketKind.Disconnect)]
    public void Encode_ThenDecode_RoundTrips_Kind(int kindValue)
    {
        var kind = (PacketKind)kindValue;
        var hdr = new PacketHeader(kind, channel: 0);
        Span<byte> buf = stackalloc byte[PacketHeader.Size];
        hdr.Encode(buf);
        Assert.True(PacketHeader.TryDecode(buf, out var decoded));
        Assert.Equal(kind, decoded.Kind);
        Assert.Equal(hdr.Version, decoded.Version);
        Assert.Equal(hdr.Channel, decoded.Channel);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)127)]
    [InlineData((byte)255)]
    public void Encode_ThenDecode_RoundTrips_Channel(byte channel)
    {
        var hdr = new PacketHeader(PacketKind.Data, channel);
        Span<byte> buf = stackalloc byte[PacketHeader.Size];
        hdr.Encode(buf);
        Assert.True(PacketHeader.TryDecode(buf, out var decoded));
        Assert.Equal(channel, decoded.Channel);
    }

    // ─── Edge / error cases ────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentOutOfRangeException_WhenVersionExceedsNibble()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PacketHeader(PacketKind.Data, channel: 0, version: 16));
    }

    [Fact]
    public void Encode_ThrowsArgumentException_WhenBufferTooShort()
    {
        var header = new PacketHeader(PacketKind.Data, channel: 0);
        // Use a heap array to avoid the "cannot use ref local inside lambda" restriction.
        var tooShort = new byte[1];

        Assert.Throws<ArgumentException>(() => header.Encode(tooShort.AsSpan()));
    }

    [Fact]
    public void DefaultVersionParameter_IsZero()
    {
        // Default version parameter must be 0 per current protocol
        var header = new PacketHeader(PacketKind.Data, channel: 0);
        Assert.Equal(0, header.Version);
    }

    [Fact]
    public void Default_PacketHeader_HasZeroVersionDataKindAndZeroChannel()
    {
        var header = default(PacketHeader);
        Assert.Equal(0, header.Version);
        Assert.Equal(PacketKind.Data, header.Kind);
        Assert.Equal((byte)0, header.Channel);
    }

    [Fact]
    public void TryDecode_NonZeroVersion_ReturnsFalse()
    {
        // version=5 in upper nibble — not protocol version 0, must be rejected
        Span<byte> buf = stackalloc byte[2];
        buf[0] = 0x51; // version=5, kind=Handshake
        buf[1] = 0x00;
        Assert.False(PacketHeader.TryDecode(buf, out _));
    }

    [Fact]
    public void TryDecode_ReservedKind_ReturnsFalse()
    {
        // kind=0x4 is reserved — must be rejected
        Span<byte> buf = stackalloc byte[2];
        buf[0] = 0x04; // version=0, kind=0x4 (reserved)
        buf[1] = 0x00;
        Assert.False(PacketHeader.TryDecode(buf, out _));
    }

    [Fact]
    public void TryDecode_MaxReservedKind_ReturnsFalse()
    {
        // kind=0xF is reserved
        Span<byte> buf = stackalloc byte[2];
        buf[0] = 0x0F; // version=0, kind=0xF (reserved)
        buf[1] = 0x00;
        Assert.False(PacketHeader.TryDecode(buf, out _));
    }
}
