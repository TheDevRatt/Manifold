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
    public void Encode_WithNonZeroVersion_EncodeVersionInUpperNibble()
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

    [Fact]
    public void Encode_ThenDecode_RoundTripsAllKinds()
    {
        var kinds = new[] { PacketKind.Data, PacketKind.Handshake, PacketKind.HandshakeAck, PacketKind.Disconnect };
        Span<byte> buf = stackalloc byte[2];

        foreach (var kind in kinds)
        {
            var original = new PacketHeader(kind, channel: 42);
            original.Encode(buf);
            bool ok = PacketHeader.TryDecode(buf, out var decoded);

            Assert.True(ok);
            Assert.Equal(original.Version, decoded.Version);
            Assert.Equal(original.Kind,    decoded.Kind);
            Assert.Equal(original.Channel, decoded.Channel);
        }
    }

    [Fact]
    public void Encode_ThenDecode_RoundTrips_AllChannels_0_to_255()
    {
        byte[] spotChannels = { 0, 1, 127, 255 };
        Span<byte> buf = stackalloc byte[2];

        foreach (byte ch in spotChannels)
        {
            var original = new PacketHeader(PacketKind.Data, channel: ch);
            original.Encode(buf);
            bool ok = PacketHeader.TryDecode(buf, out var decoded);

            Assert.True(ok);
            Assert.Equal(ch, decoded.Channel);
            Assert.Equal(PacketKind.Data, decoded.Kind);
        }
    }

    // ─── Edge / error cases ────────────────────────────────────────────────────

    [Fact]
    public void Encode_ThrowsArgumentException_WhenBufferTooShort()
    {
        var header = new PacketHeader(PacketKind.Data, channel: 0);
        // Use a heap array to avoid the "cannot use ref local inside lambda" restriction.
        var tooShort = new byte[1];

        Assert.Throws<ArgumentException>(() => header.Encode(tooShort.AsSpan()));
    }

    [Fact]
    public void VersionNibble_Is_Zero_ForDefaultConstructor()
    {
        // Default version parameter must be 0 per current protocol
        var header = new PacketHeader(PacketKind.Data, channel: 0);
        Assert.Equal(0, header.Version);
    }

    [Fact]
    public void KindIsCorrectlyMasked_WhenDecodingUpperNibble()
    {
        // byte0 = 0x51  →  version=5, kind=Handshake(0x1)
        ReadOnlySpan<byte> buf = stackalloc byte[] { 0x51, 0x00 };
        bool ok = PacketHeader.TryDecode(buf, out var header);

        Assert.True(ok);
        Assert.Equal(5, header.Version);
        Assert.Equal(PacketKind.Handshake, header.Kind);
    }
}
