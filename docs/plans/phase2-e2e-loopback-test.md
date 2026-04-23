# Phase 2 End-to-End Loopback Test Plan

> **For Hermes:** This is the manual verification gate for Phase 2. The automated FakeSteamBackend simulation covers all logic paths. The live loopback requires a real Steam session and cannot be automated without a Godot headless CI setup (Phase 3 CI task).

---

## Automated Gate (no Steam required)

Runs in CI as part of every push.

```bash
cd ~/Manifold
~/.dotnet/dotnet test src/Manifold.Core.Tests/ -v minimal
```

Expected: **197+ tests passing, 0 failures**

These tests cover:
- All `PacketHeader` encode/decode paths and nibble masking
- All `HandshakeProtocol` encode/decode + `HandshakeState` timeout
- All `PeerIdMapper` bidirectional mapping, edge cases, and duplicate guards
- `SteamNetworkingCore` host/client create/close, idempotent close, callback routing
- `ProcessMessage` with real stack-allocated `SteamNetworkingMessage_t` structs
- `SteamPeerRegistry` Register/Unregister/ShutdownAll lifecycle

---

## Manual Gate (live Steam, same machine)

### Prerequisites

1. Steam client running and logged in to a valid account
2. `steam_appid.txt` in the Godot project root containing a valid AppID:
   - Use SpaceWar (480) for development testing
   - Or your game's own AppID once registered

### Setup

1. Build and open `samples/ManifoldSample/` in Godot 4 (.NET)
2. Verify the project compiles with no errors
3. Ensure `Manifold.Core.dll` and `Manifold.Godot.dll` are in the build output

### Loopback Test Steps

**Instance A (Host):**
1. Launch from Godot editor
2. Click "Create Host" — observe `[Manifold] SteamMultiplayerPeer: CreateHost OK, listening on port 0`
3. Note the displayed Steam64 ID for Instance B

**Instance B (Client):**
1. Launch a second instance (export + run, or a second editor window)
2. Enter Instance A's Steam64 ID
3. Click "Join" — observe `[Manifold] Connecting to peer {id}...`

**Handshake verification:**
4. Both consoles should show:
   - Instance A: `[Godot] Peer 2 connected`
   - Instance B: `[Godot] Server connected (peer ID = 2)`

**Data exchange:**
5. Instance B: click "Send Test Packet" (sends `Hello from peer 2` as a reliable RPC)
6. Instance A console shows: `Received from peer 2: Hello from peer 2`

**Disconnect:**
7. Instance A: click "Disconnect"
8. Instance A: `[Godot] Peer 2 disconnected`
9. Instance B: `[Manifold] Server disconnected (wasLocal: false)` + `LastDisconnectInfo.Reason` is non-empty

### Pass Criteria

- [ ] No crashes, no exceptions in Godot output window
- [ ] `PeerConnected` signal fires on both sides
- [ ] Data packet received intact (no corruption, correct payload)
- [ ] `PeerDisconnected` signal fires on both sides on clean disconnect
- [ ] `PeerDisconnectedWithReason` signal carries `Reason` and `WasLocalClose` fields
- [ ] `SteamMultiplayerPeer.LastDisconnectInfo` is non-null after disconnect

### Known Limitations (Phase 2)

- `HostWithLobbyAsync` and `JoinLobbyAsync` return `Error.Unavailable` — requires Phase 3 `SteamMatchmaking`
- `SteamFriendsNode`, `SteamUserNode`, `SteamMatchmakingNode` not yet implemented — Phase 3
- `SteamManager` autoload not yet implemented — Phase 3
- Two-peer loopback requires manual Steam64 ID exchange — lobby-based matchmaking is Phase 3

---

## E2E Automated Simulation (FakeSteamBackend)

A full two-peer state machine simulation using `FakeSteamBackend`:

```csharp
// This test simulates the full handshake + data exchange + disconnect cycle
// using FakeSteamBackend — no live Steam required.
// Currently covered across the StateMachine/ test files.
// A single integration-level test wrapping the full flow is a Phase 3 goal
// once GdUnit4 headless test infrastructure is set up.
```

See `Manifold.Core.Tests/Networking/SteamNetworkingCoreTests.cs` for the current simulation tests.
