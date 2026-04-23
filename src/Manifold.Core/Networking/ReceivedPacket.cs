using System;

namespace Manifold.Core.Networking;

/// <summary>
/// A decoded incoming packet from the Steam receive queue.
/// <see cref="Buffer"/> is rented from <see cref="System.Buffers.ArrayPool{T}"/> and
/// must be returned by the consumer when no longer needed.
/// (MASTER_DESIGN §8.4.1, §8.4.2)
/// </summary>
internal readonly struct ReceivedPacket
{
    /// <summary>The packet payload buffer (rented from ArrayPool). Consumer must return it.</summary>
    internal byte[] Buffer { get; init; }

    /// <summary>The number of valid bytes in <see cref="Buffer"/>.</summary>
    internal int Size { get; init; }

    /// <summary>The HSteamNetConnection handle that sent this packet.</summary>
    internal uint Connection { get; init; }

    /// <summary>The decoded channel index from the 2-byte Manifold header.</summary>
    internal byte Channel { get; init; }

    /// <summary>The decoded packet kind from the 2-byte Manifold header.</summary>
    internal PacketKind Kind { get; init; }
}
