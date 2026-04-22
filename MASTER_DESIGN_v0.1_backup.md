# Manifold — Master Design Document
**A Godot-First C# Steamworks SDK Library**

> Version: 0.1 (Draft)
> Author: Matthew Makary
> Last Updated: 2026-04-21
> Status: Pre-Development / Architecture Phase

---

## 1. Project Vision

Manifold is a Godot-first, C# native library that exposes the Steamworks SDK directly to Godot 4 game developers. It is not a wrapper around GodotSteam, not a port of Steamworks.NET, and not a generated binding dump. It is a ground-up, idiomatic C# library designed to feel native to developers who already know Godot 4's multiplayer system.

### Core Principles

- **Godot-first**: APIs are designed around Godot's conventions — `MultiplayerPeerExtension`, `[Signal]`, `Node` lifecycle, `ProjectSettings`, autoloads. Not around Steamworks C++ conventions.
- **No intermediary**: P/Invoke directly against `steam_api64.dll` / `libsteam_api.so` via Valve's flat C API (`steam_api_flat.h`). No GodotSteam, no Steamworks.NET as a dependency.
- **Modern C#**: `async`/`await` for call results, C# `event` for recurring callbacks, nullable reference types, records for Steam data structures.
- **100% test coverage**: Every public API surface has unit and integration tests.
- **Best developer experience**: Bundled native binaries, IntelliSense-complete XML docs on every public member, NuGet distribution, clear error messages.

---

## 2. Name & Identity

| Property | Value |
|---|---|
| Library Name | **Manifold** |
| NuGet Package ID | `Manifold.Godot` |
| Root Namespace | `Manifold` |
| GitHub Repo | `TBD` |
| Target SDK | Steamworks SDK 1.64 |
| Target Engine | Godot 4.3+ (.NET / C#) |
| .NET Target | net8.0 |
| License | MIT |

### Namespace Structure

```
Manifold
├── Manifold.Core              ← Init, shutdown, callback dispatch, SteamId types
├── Manifold.Networking        ← SteamMultiplayerPeer, SteamLobbySession, PeerIdMapper
├── Manifold.Friends           ← ISteamFriends wrappers
├── Manifold.Matchmaking       ← ISteamMatchmaking (lobbies)
├── Manifold.User              ← ISteamUser (identity, auth tickets)
├── Manifold.Utils             ← ISteamUtils, ISteamNetworkingUtils
├── Manifold.Interop           ← All P/Invoke declarations (generated)
└── Manifold.Testing           ← Test helpers, mock Steam backend
```

---

## 3. SDK & Platform Support

### Steamworks SDK Version
- **Primary target**: SDK 1.64 (bundled in repo)
- **Upgrade path**: Re-run source generator when new SDK ships

### Platform Support Matrix

| Platform | Native Binary | P/Invoke Target |
|---|---|---|
| Windows x64 | `steam_api64.dll` | ✅ Primary |
| Linux x64 | `libsteam_api.so` | ✅ Primary |
| macOS | `libsteam_api.dylib` | ✅ Primary |
| Windows x32 | `steam_api.dll` | ⚠️ Best-effort |
| Android ARM64 | `libsteam_api.so` | ❌ Out of scope (Steam not supported on Android) |

### Godot Compatibility
- **Minimum**: Godot 4.3 (.NET)
- **Tested against**: Godot 4.6.2
- `MultiplayerPeerExtension` API stable since Godot 4.0 — no breaking changes expected

---

## 4. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        Your Game Code                           │
│   Rpc(), RpcId(), Multiplayer.MultiplayerPeer = steamPeer      │
└───────────────────────────────┬─────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────┐
│                    Manifold.Networking                           │
│   SteamMultiplayerPeer  SteamLobbySession  PeerIdMapper        │
└───────────────────────────────┬─────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────┐
│                      Manifold.Core                              │
│   SteamManager (Autoload)   CallbackDispatcher   SteamId       │
│   CallResultAwaiter<T>      SteamException                     │
└───────────────────────────────┬─────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────┐
│                     Manifold.Interop                            │
│   [Generated P/Invoke declarations from steam_api_flat.h]      │
│   NativeCallbacks   NativeStructs   NativeEnums                │
└───────────────────────────────┬─────────────────────────────────┘
                                │
                    ┌───────────▼──────────┐
                    │  steam_api64.dll     │
                    │  libsteam_api.so     │
                    │  libsteam_api.dylib  │
                    └──────────────────────┘
```

---

## 5. P/Invoke Generation Strategy

### Source of Truth
`sdk/public/steam/steam_api.json` — Valve's machine-readable schema of the entire API. Contains:
- All interfaces and their methods (name, args, return types, call convention)
- All structs and their fields
- All enums and their values
- All callback types (with `k_iCallback` discriminant values)
- Call result types

### Generator Design
A standalone C# console tool (`tools/ManifoldGen/`) that:

1. **Reads** `steam_api.json`
2. **Maps** C types → C# types (see type mapping table below)
3. **Emits** a `Manifold.Interop` source file containing:
   - `[DllImport]` P/Invoke declarations for all flat API functions
   - `[StructLayout(LayoutKind.Sequential)]` structs for all Steam structs
   - C# `enum` types for all Steam enums
   - Callback ID constants (`k_iCallback`) as a static class

4. **Is idempotent**: running it again on a new SDK version regenerates cleanly

### C → C# Type Mapping

| C Type | C# Type | Notes |
|---|---|---|
| `uint64_steamid` | `SteamId` (custom struct wrapping ulong) | Strong typing, not raw ulong |
| `uint64` | `ulong` | |
| `uint32` | `uint` | |
| `int32` | `int` | |
| `uint16` | `ushort` | |
| `uint8` | `byte` | |
| `bool` | `bool` | — careful: Steam bools are 1 byte |
| `const char*` | `[MarshalAs(UnmanagedType.LPUTF8Str)] string` | |
| `void*` | `IntPtr` | |
| `HSteamNetConnection` | `uint` (typedef) | |
| `SteamAPICall_t` | `ulong` (typedef) | Used internally by CallResultAwaiter |
| Enum types | Generated C# `enum` | Underlying int or uint depending on SDK |
| Struct pointers | `ref T` or `IntPtr` | Based on usage context |

### Generator Output Example

```csharp
// AUTO-GENERATED — DO NOT EDIT
// Source: steamworks_sdk_164/sdk/public/steam/steam_api.json
// Generator: ManifoldGen 1.0.0

namespace Manifold.Interop;

internal static partial class SteamNative
{
    private const string LibName = "steam_api64"; // resolved per platform

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SteamAPI_SteamNetworkingSockets_v012();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2P(
        IntPtr self,
        int nLocalVirtualPort,
        int nOptions,
        IntPtr pOptions);

    // ... etc
}
```

---

## 6. Core Systems

### 6.1 SteamManager (Autoload Node)

The central autoload. Handles the full Steam lifecycle so developers never have to wire it manually.

```csharp
// Usage — just add SteamManager as an autoload in Project Settings
public partial class SteamManager : Node
{
    public static SteamManager Instance { get; private set; }

    // Sub-interfaces — lazily initialized
    public SteamUser       User          { get; private set; }
    public SteamFriends    Friends       { get; private set; }
    public SteamMatchmaking Matchmaking  { get; private set; }
    public SteamNetworking Networking    { get; private set; }
    public SteamUtils      Utils         { get; private set; }

    // Signals
    [Signal] public delegate void SteamInitializedEventHandler();
    [Signal] public delegate void SteamInitFailedEventHandler(string reason);
    [Signal] public delegate void SteamShutdownEventHandler();

    public bool IsInitialized { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        TryInitialize();
    }

    public override void _Process(double delta)
    {
        if (IsInitialized)
            CallbackDispatcher.RunCallbacks(); // SteamAPI_ManualDispatch_RunFrame
    }

    public override void _ExitTree()
    {
        if (IsInitialized)
            SteamNative.SteamAPI_Shutdown();
    }
}
```

**Project Settings Integration:**

```
steam/app_id            (int)  — reads from steam_appid.txt if not set
steam/auto_initialize   (bool) — default true
steam/warn_if_not_running (bool) — default true (editor warning if Steam not open)
```

### 6.2 Callback Dispatcher

Steam's flat API provides `SteamAPI_ManualDispatch_*` for explicit callback control. The dispatcher:

- Calls `SteamAPI_ManualDispatch_RunFrame()` each frame (invoked by SteamManager._Process)
- Drains the callback pipe via `SteamAPI_ManualDispatch_GetNextCallback`
- Routes each callback by `k_iCallback` discriminant to registered C# `event` handlers
- Handles `SteamAPICall_t` completions by resolving the matching `TaskCompletionSource<T>`

```csharp
internal static class CallbackDispatcher
{
    // Maps callback ID → list of handlers
    private static readonly Dictionary<int, List<Action<IntPtr>>> _handlers = new();

    // Maps SteamAPICall_t handle → TaskCompletionSource
    private static readonly Dictionary<ulong, Action<IntPtr, bool>> _callResults = new();

    internal static void Register(int callbackId, Action<IntPtr> handler) { ... }
    internal static void RegisterCallResult(ulong apiCall, Action<IntPtr, bool> handler) { ... }
    internal static void RunCallbacks() { ... }
}
```

### 6.3 CallResultAwaiter\<T\>

Wraps a `SteamAPICall_t` handle into a `Task<T>` so call results are async/await compatible:

```csharp
// Internal usage
internal static Task<T> Await<T>(ulong apiCall) where T : unmanaged
{
    var tcs = new TaskCompletionSource<T>();
    CallbackDispatcher.RegisterCallResult(apiCall, (ptr, ioFailed) =>
    {
        if (ioFailed)
            tcs.SetException(new SteamCallFailedException());
        else
            tcs.SetResult(Marshal.PtrToStructure<T>(ptr));
    });
    return tcs.Task;
}

// Developer-facing usage
LobbyCreated_t result = await SteamManager.Instance.Matchmaking.CreateLobbyAsync(
    ELobbyType.Public, maxMembers: 4);
```

### 6.4 SteamId Type

A strongly-typed wrapper around Steam's 64-bit ID, replacing raw `ulong`:

```csharp
public readonly record struct SteamId(ulong Value)
{
    public static readonly SteamId Invalid = new(0);
    public bool IsValid => Value != 0;
    public static implicit operator ulong(SteamId id) => id.Value;
    public static implicit operator SteamId(ulong value) => new(value);
    public override string ToString() => Value.ToString();
}
```

---

## 7. Networking Layer

### 7.1 SteamMultiplayerPeer

The crown jewel of Manifold. Implements `MultiplayerPeerExtension` so it is a drop-in replacement for `ENetMultiplayerPeer`.

```csharp
public partial class SteamMultiplayerPeer : MultiplayerPeerExtension
{
    // ── Setup ───────────────────────────────────────────────────
    public Error CreateHost(int virtualPort = 0);
    public Error CreateClient(SteamId hostSteamId, int virtualPort = 0);
    public Task<Error> HostWithLobbyAsync(ELobbyType type, int maxMembers);
    public Task<Error> JoinLobbyAsync(SteamId lobbyId);

    // ── Config ──────────────────────────────────────────────────
    public bool NoNagle         { get; set; } = false;
    public bool NoDelay         { get; set; } = false;
    public bool UseRelay        { get; set; } = true;

    // ── MultiplayerPeerExtension overrides ──────────────────────
    public override void  _Poll();
    public override Error _GetPacket(out byte[] rBuffer, out int rBufferSize);
    public override Error _PutPacket(byte[] pBuffer, int pBufferSize);
    public override int   _GetAvailablePacketCount();
    public override int   _GetMaxPacketSize();
    public override int   _GetUniqueId();
    public override int   _GetPacketPeer();
    public override int   _GetPacketChannel();
    public override MultiplayerPeer.TransferModeEnum _GetPacketMode();
    public override void  _SetTransferChannel(int pChannel);
    public override int   _GetTransferChannel();
    public override void  _SetTransferMode(MultiplayerPeer.TransferModeEnum pMode);
    public override MultiplayerPeer.TransferModeEnum _GetTransferMode();
    public override void  _SetTargetPeer(int pPeer);
    public override MultiplayerPeer.ConnectionStatus _GetConnectionStatus();
    public override bool  _IsServer();
    public override bool  _IsServerRelaySupported(); // returns true
    public override bool  _IsRefusingNewConnections();
    public override void  _SetRefuseNewConnections(bool pEnable);
    public override void  _Close();
    public override void  _DisconnectPeer(int pPeer, bool pForce);
}
```

**Internal Packet Queue:**

```csharp
private record struct IncomingPacket(
    byte[] Data,
    int Sender,           // Godot peer ID
    int Channel,
    MultiplayerPeer.TransferModeEnum Mode
);

private readonly Queue<IncomingPacket> _incoming = new();
private IncomingPacket _current;
```

**Channel Strategy:**
Steam's `ISteamNetworkingSockets` does not have native channels. Manifold emulates them by prepending a 1-byte channel tag to every packet payload. This is transparent to the developer.

**SteamID → Godot Peer ID Mapping:**
Steam IDs are 64-bit; Godot peer IDs are 32-bit. Handled by `PeerIdMapper`:

```csharp
internal sealed class PeerIdMapper
{
    private readonly Dictionary<SteamId, int> _steamToGodot = new();
    private readonly Dictionary<int, SteamId> _godotToSteam = new();
    private int _nextId = 2; // 1 is always the server

    public int Register(SteamId steamId) { ... }
    public SteamId GetSteamId(int godotId) { ... }
    public int GetGodotId(SteamId steamId) { ... }
    public void Remove(int godotId) { ... }
}
```

**Peer ID Handshake Protocol:**
Because clients don't know their Godot peer ID until the server assigns it:

1. Client connects via Steam relay
2. Server assigns an int ID, serializes it into a special handshake packet (1-byte header `0xFF` = handshake)
3. Client receives handshake, stores its `_uniqueId`, emits `PeerConnected(1)` (server)
4. Server emits `PeerConnected(clientId)`
5. Normal RPC traffic begins

### 7.2 SteamLobbySession

A higher-level convenience class that ties a Steam lobby to a `SteamMultiplayerPeer`. Owns the full session lifecycle:

```csharp
public partial class SteamLobbySession : RefCounted
{
    public SteamId LobbyId     { get; private set; }
    public SteamId OwnerId     { get; private set; }
    public int     MemberCount { get; private set; }
    public int     MaxMembers  { get; private set; }

    [Signal] public delegate void MemberJoinedEventHandler(SteamId steamId);
    [Signal] public delegate void MemberLeftEventHandler(SteamId steamId);
    [Signal] public delegate void OwnerChangedEventHandler(SteamId newOwner);
    [Signal] public delegate void LobbyClosedEventHandler();

    // Metadata
    public string GetData(string key);
    public void   SetData(string key, string value);

    // Peer
    public SteamMultiplayerPeer Peer { get; }
}
```

---

## 8. API Layer (Manifold.Friends, Manifold.Matchmaking, etc.)

Each interface wrapper follows the same pattern:

- **Synchronous methods** for non-blocking calls (e.g. `GetPersonaName()`, `GetSteamId()`)
- **Async Task\<T\> methods** for call results (e.g. `CreateLobbyAsync()`, `RequestLobbyListAsync()`)
- **C# events** for recurring callbacks (e.g. `PersonaStateChanged`, `LobbyDataUpdated`)
- **Strongly typed returns** — no `Dictionary`, no raw `Variant`, no `long` where an enum belongs

### Example: SteamMatchmaking

```csharp
public sealed class SteamMatchmaking
{
    // Call results (async)
    public Task<LobbyCreated>      CreateLobbyAsync(ELobbyType type, int maxMembers);
    public Task<LobbyEnterResult>  JoinLobbyAsync(SteamId lobbyId);
    public Task<LobbyList>         RequestLobbyListAsync();

    // Non-blocking
    public void      LeaveLobby(SteamId lobbyId);
    public int       GetNumLobbyMembers(SteamId lobbyId);
    public SteamId   GetLobbyMemberByIndex(SteamId lobbyId, int index);
    public SteamId   GetLobbyOwner(SteamId lobbyId);
    public string    GetLobbyData(SteamId lobbyId, string key);
    public bool      SetLobbyData(SteamId lobbyId, string key, string value);
    public bool      SetLobbyMemberLimit(SteamId lobbyId, int maxMembers);
    public bool      SetLobbyType(SteamId lobbyId, ELobbyType type);
    public bool      SetLobbyJoinable(SteamId lobbyId, bool joinable);
    public void      InviteUserToLobby(SteamId lobbyId, SteamId invitee);

    // Recurring callbacks (C# events)
    public event Action<LobbyDataUpdate>   LobbyDataUpdated;
    public event Action<LobbyChatUpdate>   LobbyChatUpdated;
    public event Action<LobbyInvite>       LobbyInviteReceived;
    public event Action<GameLobbyJoinRequested> JoinRequested; // Steam overlay join
}
```

---

## 9. Testing Strategy

### Coverage Target: 100%

Every public class, method, property, and event must have test coverage. No exceptions.

### Test Layers

| Layer | Tool | What it Tests |
|---|---|---|
| Unit | xUnit + Moq | Pure C# logic — PeerIdMapper, packet queue, type mapping, handshake protocol, CallResultAwaiter |
| Interop | xUnit + real DLL | P/Invoke declarations against the actual `steam_api64.dll` (Steam not required — uses `SteamAPI_InitFlat` offline path) |
| Integration | xUnit + SpaceWar AppID | Full end-to-end lobby creation, peer connection, RPC dispatch (requires Steam running) |
| Godot | GdUnit4 (C# runner) | `SteamMultiplayerPeer` inside a headless Godot process, `@rpc` round-trip |

### Test Project Structure

```
Manifold.Tests/
├── Core/
│   ├── CallbackDispatcherTests.cs
│   ├── CallResultAwaiterTests.cs
│   ├── SteamIdTests.cs
│   └── SteamManagerTests.cs
├── Networking/
│   ├── PeerIdMapperTests.cs
│   ├── SteamMultiplayerPeerTests.cs
│   ├── HandshakeProtocolTests.cs
│   └── PacketQueueTests.cs
├── Matchmaking/
│   └── SteamMatchmakingTests.cs
├── Friends/
│   └── SteamFriendsTests.cs
├── Interop/
│   └── PInvokeDeclarationTests.cs
└── TestHelpers/
    ├── MockSteamBackend.cs      ← Fake P/Invoke layer for unit tests
    └── SteamTestFixture.cs      ← xUnit fixture that inits Steam once per suite
```

### Mock Strategy

Since most tests can't depend on a live Steam session, we introduce an `ISteamNativeBackend` interface that the P/Invoke layer implements. Unit tests substitute a `MockSteamBackend` that returns controlled data without hitting any DLL.

---

## 10. Distribution & Packaging

### NuGet Package: `Manifold.Godot`

**Package Contents:**

```
Manifold.Godot.nupkg
├── lib/
│   └── net8.0/
│       └── Manifold.dll
├── runtimes/
│   ├── win-x64/native/
│   │   └── steam_api64.dll
│   ├── linux-x64/native/
│   │   └── libsteam_api.so
│   └── osx/native/
│       └── libsteam_api.dylib
├── build/
│   └── Manifold.Godot.targets    ← MSBuild targets to copy natives to output dir
└── README.md
```

**MSBuild Targets** ensure `steam_api64.dll` / `libsteam_api.so` are automatically copied to the Godot project's output directory on build — zero manual setup for the developer.

### Versioning
`MAJOR.MINOR.PATCH-sdkVERSION`
Example: `1.0.0-sdk164`

SDK major bumps trigger a MINOR version increment of Manifold.

---

## 11. Project Structure

```
Project Steamworks/
├── MASTER_DESIGN.md              ← This document
├── steamworks_sdk_164/           ← Valve SDK (source of truth)
├── src/
│   ├── Manifold/                 ← Main library project
│   │   ├── Manifold.csproj
│   │   ├── Core/
│   │   │   ├── SteamManager.cs
│   │   │   ├── CallbackDispatcher.cs
│   │   │   ├── CallResultAwaiter.cs
│   │   │   ├── SteamId.cs
│   │   │   └── SteamException.cs
│   │   ├── Networking/
│   │   │   ├── SteamMultiplayerPeer.cs
│   │   │   ├── SteamLobbySession.cs
│   │   │   └── PeerIdMapper.cs
│   │   ├── Matchmaking/
│   │   │   ├── SteamMatchmaking.cs
│   │   │   └── Models/            ← LobbyCreated, LobbyEnterResult, etc.
│   │   ├── Friends/
│   │   │   ├── SteamFriends.cs
│   │   │   └── Models/
│   │   ├── User/
│   │   │   ├── SteamUser.cs
│   │   │   └── Models/
│   │   ├── Utils/
│   │   │   └── SteamUtils.cs
│   │   └── Interop/              ← GENERATED — do not hand-edit
│   │       ├── SteamNative.cs
│   │       ├── NativeStructs.cs
│   │       ├── NativeEnums.cs
│   │       └── NativeCallbacks.cs
│   └── Manifold.Tests/
│       ├── Manifold.Tests.csproj
│       └── [test files per section 9]
├── tools/
│   └── ManifoldGen/              ← P/Invoke source generator
│       ├── ManifoldGen.csproj
│       ├── Program.cs
│       ├── JsonModels.cs         ← Deserializes steam_api.json
│       ├── TypeMapper.cs         ← C → C# type mapping
│       └── Emitters/
│           ├── PInvokeEmitter.cs
│           ├── StructEmitter.cs
│           ├── EnumEmitter.cs
│           └── CallbackEmitter.cs
├── samples/
│   └── ManifoldSample/           ← Godot 4 sample project
│       └── [Godot project demonstrating lobby + RPC]
└── docs/
    ├── getting-started.md
    ├── multiplayer-peer.md
    ├── lobby-sessions.md
    ├── callbacks-and-async.md
    └── api/                      ← Auto-generated API reference
```

---

## 12. Development Phases

### Phase 1 — Foundation (Current)
- [ ] Set up solution structure (`src/Manifold`, `Manifold.Tests`, `tools/ManifoldGen`)
- [ ] Write `ManifoldGen` — reads `steam_api.json`, emits `Manifold.Interop`
- [ ] Implement `SteamId`, `SteamException`, basic type infrastructure
- [ ] Implement `CallbackDispatcher` with `SteamAPI_ManualDispatch_*`
- [ ] Implement `CallResultAwaiter<T>`
- [ ] Implement `SteamManager` autoload node
- [ ] Write unit tests for all Phase 1 components
- [ ] Confirm `SteamAPI_Init` → `SteamAPI_Shutdown` round-trip works via P/Invoke

### Phase 2 — Networking Core
- [ ] Implement `PeerIdMapper`
- [ ] Implement handshake protocol
- [ ] Implement `SteamMultiplayerPeer` (all `MultiplayerPeerExtension` overrides)
- [ ] Implement `SteamLobbySession`
- [ ] Integration test: two peers connect, exchange RPC, disconnect cleanly

### Phase 3 — API Surface
- [ ] `SteamUser` — identity, auth tickets
- [ ] `SteamFriends` — persona, rich presence
- [ ] `SteamMatchmaking` — lobby CRUD, metadata, invite flow
- [ ] `SteamUtils` — overlay, app ID, country

### Phase 4 — Polish & Distribution
- [ ] NuGet packaging with native binary bundling
- [ ] MSBuild targets for automatic native copy-to-output
- [ ] Sample Godot project (lobby browser + multiplayer demo)
- [ ] Full documentation
- [ ] GitHub Actions CI: build + test on Windows, Linux, macOS

---

## 13. Key Technical Decisions & Rationale

| Decision | Choice | Rationale |
|---|---|---|
| P/Invoke source | `steam_api_flat.h` / `steam_api.json` | Valve provides the flat API specifically for language bindings. Machine-readable JSON allows code generation. |
| Callback dispatch | `SteamAPI_ManualDispatch_*` | Gives full control over when callbacks fire. Safe for Godot's single-threaded scene tree. |
| Call results | `Task<T>` | Modern C# idiom. Composable with `async`/`await`. Avoids callback hell. |
| Channel emulation | 1-byte channel prefix on packets | Simple, zero overhead, fully transparent. Avoids needing multiple listen sockets. |
| Peer ID mapping | Bidirectional `Dictionary<SteamId, int>` | O(1) lookup both ways. Godot needs int IDs; Steam uses 64-bit IDs. |
| Relay topology | `_IsServerRelaySupported() = true` | Clients route through server — simpler NAT traversal, matches Steam's relay network model. |
| Thread model | Main thread only | Godot scene tree is single-threaded. All Steam calls and callbacks on main thread via `_Process`. |
| No GodotSteam dependency | Direct SDK | Removes the GDExtension binary dependency, reduces distribution complexity, full control. |
| Mock backend interface | `ISteamNativeBackend` | Enables unit tests without a live Steam session or DLL present. |

---

## 14. Open Questions

- [ ] **Library name confirmed?** — `Manifold` pending Matthew's approval
- [ ] **Game server support in v1?** — `ISteamGameServer` / `SteamGameServer_RunCallbacks` — defer to Phase 5?
- [ ] **Facepunch.Steamworks interop?** — Some devs may use both. Document incompatibility or provide a compatibility shim?
- [ ] **Editor plugin?** — A Godot editor plugin to validate `steam_appid.txt`, show Steam status in the editor — nice-to-have for v1?
- [ ] **GDScript bridge?** — Should Manifold's high-level types be exposed to GDScript via `[GlobalClass]`? Or is this strictly C#-only?

---

*This document is the canonical reference for Manifold's design. All implementation decisions should be reconciled against it. Update this document before implementing any significant architectural change.*
