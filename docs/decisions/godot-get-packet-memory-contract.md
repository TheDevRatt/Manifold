# Decision: Godot _GetPacket Memory Contract

Date: 2026-04-23
Investigated by: Hermes subagent (Task 6)

## Question

Does `MultiplayerPeerExtension._GetPacket` allow the returned `byte[]` to be immediately
returned to `ArrayPool<byte>` after `_GetPacket` returns, or does Godot hold the reference
and access it later?

## Finding

**HOLD** — but only until the _next_ call to `get_packet`. Godot accesses the buffer pointer
**synchronously within the same `Poll()` iteration** before calling `get_packet` again.
ArrayPool return is **UNSAFE** unless you can guarantee no re-entry, but the contract is
subtler than a simple COPY/HOLD:

> The buffer is valid from the moment `_GetPacket` returns until the next call to `_GetPacket`.

This means: returning the buffer to `ArrayPool` at the **end of `_GetPacket`** (before Godot
has read the data) is **UNSAFE**. The safe strategy is to keep a persistent buffer and never
use `ArrayPool` at all.

## Evidence

### 1. Godot source comment — `core/io/packet_peer.h` (master branch)

```cpp
virtual Error get_packet(const uint8_t **r_buffer, int &r_buffer_size) = 0;
///< buffer is GONE after next get_packet
```

URL: https://github.com/godotengine/godot/blob/master/core/io/packet_peer.h

This is the **official documented contract** in the C++ base class. The comment reads
"buffer is GONE after next `get_packet`" — meaning the buffer returned by your
implementation only needs to stay valid **until the next call to `get_packet`**, not
indefinitely. But crucially, it must be valid *after* your `_GetPacket` virtual returns,
because Godot reads from the pointer immediately after the call.

This same comment is repeated on the override in `scene/main/multiplayer_peer.h`:
```cpp
virtual Error get_packet(const uint8_t **r_buffer, int &r_buffer_size) override;
///< buffer is GONE after next get_packet
```

### 2. SceneMultiplayer call site — `modules/multiplayer/scene_multiplayer.cpp`

```cpp
const uint8_t *packet;
int len;

Error err = multiplayer_peer->get_packet(&packet, len);
ERR_FAIL_COND_V_MSG(err != OK, err, ...);

// ... packet is read synchronously here, within the same stack frame ...
_process_packet(sender, packet, len);   // or _process_sys(...)
// loop continues → get_packet called again → buffer can be freed
```

The packet pointer is passed to `_process_packet` / `_process_sys` / `_process_raw` which
all process data **synchronously** (no deferred reads, no async callbacks that capture the
pointer). All data is either:
- Read inline (`packet[0] & CMD_MASK`, etc.)  
- `memcpy`-ed into a new `PackedByteArray` / `Vector<uint8_t>` before any callback fires

URL: https://github.com/godotengine/godot/blob/master/modules/multiplayer/scene_multiplayer.cpp

### 3. ENet reference implementation — `modules/enet/enet_multiplayer_peer.cpp`

The built-in ENet peer confirms the expected pattern. It stores the current packet in a
`current_packet` member and frees it on the **next** call to `get_packet` via
`_pop_current_packet()`:

```cpp
Error ENetMultiplayerPeer::get_packet(const uint8_t **r_buffer, int &r_buffer_size) {
    _pop_current_packet();           // FREE the previous buffer here

    current_packet = incoming_packets.front()->get();
    incoming_packets.pop_front();

    *r_buffer = (const uint8_t *)(current_packet.packet->data);
    r_buffer_size = current_packet.packet->dataLength;

    return OK;
}
```

This is canonical: the buffer lives from the moment `get_packet` returns until it is called
again. The `r_buffer` pointer must remain stable across that window.

URL: https://github.com/godotengine/godot/blob/master/modules/enet/enet_multiplayer_peer.cpp

### 4. `_GetPacket` vs `_GetPacketScript` paths

For **C# / GDExtension** implementations using the native `_get_packet` virtual (not the
GDScript `_get_packet_script`), the engine calls through `MultiplayerPeerExtension::get_packet`
which passes `r_buffer` and `&r_buffer_size` directly to the virtual:

```cpp
// scene/main/multiplayer_peer.cpp
Error MultiplayerPeerExtension::get_packet(const uint8_t **r_buffer, int &r_buffer_size) {
    Error err;
    if (GDVIRTUAL_CALL(_get_packet, r_buffer, &r_buffer_size, err)) {
        return err;
    }
    // ...
}
```

The C# side sets `rBuffer` (a `byte[]`) and `rBufferSize`, and the engine then **reads
`rBuffer` through the unsafe pointer** that was written by the C# virtual. The buffer must
remain valid (unpinned and unreturned) for the full synchronous dispatch of the packet.

## Conclusion

**ArrayPool return in `_GetPacket` is UNSAFE.**

The buffer pointer written to `rBuffer` must remain valid and stable **after `_GetPacket`
returns** because Godot reads from that pointer synchronously before calling `_GetPacket`
again. Returning the buffer to `ArrayPool` at the end of `_GetPacket` would create a
use-after-free: the engine holds a raw `uint8_t*` into managed memory that has been
returned to the pool and could be reallocated.

## Consequence for SteamMultiplayerPeer

### Chosen Strategy: Persistent Pre-Allocated Receive Buffer

Allocate **one persistent `byte[]`** at connection time, sized to `_GetMaxPacketSize()`
(Steam's limit is 1,048,576 bytes = 1 MB). Reuse this buffer for every `_GetPacket` call.

```csharp
private byte[] _receiveBuffer = null!;

public override Error _Ready()
{
    _receiveBuffer = new byte[_GetMaxPacketSize()];
    return Error.Ok;
}

public override Error _GetPacket(out byte[] rBuffer, out int rBufferSize)
{
    // Write received Steam packet data directly into _receiveBuffer
    // rBuffer points to _receiveBuffer — valid until next _GetPacket call
    rBuffer = _receiveBuffer;
    rBufferSize = /* actual data size */;
    return Error.Ok;
}
```

**Rationale:**
- No `ArrayPool` — eliminates the UNSAFE return-before-read race
- Allocation-free per-frame — no GC pressure during gameplay
- 1 MB per peer — acceptable for the expected peer count (≤ 64 peers = 64 MB maximum,
  though in practice only active-receive peers need this buffer)
- Matches the ENet canonical pattern (persistent `current_packet` member)

### Why not ArrayPool at all?

`ArrayPool.Rent` + `ArrayPool.Return` would be safe if and only if the return happened
**after** Godot has finished reading (i.e., after the next `_GetPacket` call). This creates
a complex lifecycle that is error-prone and not supported by the `_GetPacket` API contract.
The persistent buffer approach is simpler, equally allocation-free, and aligned with how
the engine's own built-in peers work.
