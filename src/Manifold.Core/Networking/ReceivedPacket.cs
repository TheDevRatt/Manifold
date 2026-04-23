using System;

namespace Manifold.Core.Networking;

/// <summary>
/// A decoded incoming packet from the Steam receive queue.
/// <para>
/// <see cref="Buffer"/> is rented from <see cref="System.Buffers.ArrayPool{T}.Shared"/>.
/// Consumers are responsible for returning it when done.
/// </para>
/// <para>
/// <b>Note on _GetPacketScript:</b> <c>SteamMultiplayerPeer._GetPacketScript</c> copies
/// the payload into a fresh <c>byte[]</c> before returning to Godot and immediately returns
/// this rented buffer. The Godot-facing allocation is a fresh copy — not the rented buffer.
/// This is correct because the C# <c>_GetPacketScript</c> override hands Godot a managed
/// GC reference (not a raw pointer), so Godot's reference alone keeps the copy alive.
/// See <c>docs/decisions/godot-get-packet-memory-contract.md</c>.
/// </para>
/// (MASTER_DESIGN §8.4.1)
/// </summary>
internal readonly struct ReceivedPacket
{
    /// <summary>
    /// The packet payload buffer (rented from ArrayPool). Consumer must return it.
    /// <para>
    /// When consumed by <c>SteamMultiplayerPeer._GetPacketScript</c>, this buffer is
    /// copied into a fresh <c>byte[]</c> and then immediately returned to the pool —
    /// before <c>_GetPacketScript</c> returns to Godot. The copy (not this buffer)
    /// is the Godot-facing allocation.
    /// </para>
    /// </summary>
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
