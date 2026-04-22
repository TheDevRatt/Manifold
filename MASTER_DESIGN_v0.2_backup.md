# Manifold — Master Design Document
**A Godot-First C# Steamworks SDK Library**

> Version: 0.2
> Author: Matthew Makary
> Last Updated: 2026-04-21
> Status: Pre-Development / Architecture Phase
> Previous Version: MASTER_DESIGN_v0.1_backup.md

---

## 1. Project Vision

Manifold is a C# library that exposes the Steamworks SDK to Godot 4 game developers. It is split into two layers: a **Godot-agnostic core** (`Manifold.Core`) and a **Godot integration layer** (`Manifold.Godot`). The core can be used, tested, and reasoned about without any dependency on Godot types. The Godot layer wires the core into Godot's multiplayer system, node lifecycle, and project settings.

It is not a wrapper around GodotSteam, not a port of Steamworks.NET, and not a generated binding dump. It is a ground-up, idiomatic C# library designed to feel native to Godot 4 C# developers.

### Core Principles

- **Engine-agnostic core**: `Manifold.Core` has zero Godot dependency. Steam API wrappers, the P/Invoke layer, callback dispatch, and type definitions live here. This makes unit testing fast and cheap, and leaves the door open for other engines or headless server use.
- **Godot-first integration**: `Manifold.Godot` wires the core into Godot's conventions — `MultiplayerPeerExtension`, `[Signal]`, node lifecycle, `ProjectSettings`, autoloads. Godot types never leak into the core.
- **No intermediary**: P/Invoke directly against `steam_api64.dll` / `libsteam_api.so` via Valve's flat C API (`steam_api_flat.h`). No GodotSteam, no Steamworks.NET as a dependency.
- **Modern C#**: `async`/`await` for call results, C# `event` for recurring callbacks in the core layer, Godot `[Signal]` for Godot-facing components. Nullable reference types. Records for Steam data structures.
- **Lifecycle flexibility**: The `SteamManager` autoload is the ergonomic default, not the only valid lifecycle model. The core provides `SteamLifecycle` as a standalone, engine-agnostic initializer that any host can use.
- **100% meaningful coverage**: Test coverage is contract-oriented — every public API surface, interop declaration, and state machine path is verified. Coverage numbers are a floor, not a ceiling.
- **Best developer experience**: Bundled native binaries, IntelliSense-complete XML docs on every public member, NuGet distribution, clear error messages, and zero manual wiring.

---

## 2. Name & Identity

| Property | Value |
|---|---|
| Library Name | **Manifold** |
| NuGet Package ID (core) | `Manifold.Core` |
| NuGet Package ID (Godot) | `Manifold.Godot` |
| Root Namespace | `Manifold` |
| GitHub Repo | TBD |
| Target SDK | Steamworks SDK 1.64 |
| Target Engine | Godot 4.3+ (.NET / C#) |
| .NET Target | net8.0 |
| License | MIT |

---

## 3. Package Split & Dependency Graph

```
Manifold.Core                  ← No Godot dependency. Pure .NET.
├── Manifold.Core.Interop      ← Generated P/Invoke layer
├── Manifold.Core.Networking   ← Steam networking, lobby, peer mapping (no Godot types)
├── Manifold.Core.Friends      ← ISteamFriends wrappers
├── Manifold.Core.Matchmaking  ← ISteamMatchmaking wrappers
├── Manifold.Core.User         ← ISteamUser wrappers
└── Manifold.Core.Utils        ← ISteamUtils, ISteamNetworkingUtils

Manifold.Godot                 ← Depends on Manifold.Core + GodotSharp
├── SteamMultiplayerPeer       ← MultiplayerPeerExtension impl, wraps Core networking
├── SteamLobbySession          ← Godot RefCounted, wraps Core lobby
├── SteamManager               ← Godot Node autoload (ergonomic default lifecycle)
└── SteamSignalBridge          ← Translates Core C# events → Godot [Signal]
```

### The Rule on Events vs Signals

**Core layer (`Manifold.Core`):** All callbacks are exposed as C# `event Action<T>`. No Godot types involved.

**Godot layer (`Manifold.Godot`):** Godot-facing components (`SteamManager`, `SteamLobbySession`, etc.) expose Godot `[Signal]` declarations. Internally they subscribe to the corresponding core `event` and re-emit as a Godot signal. This means both C# events and Godot signals are always available — the core event for typed C# code, the Godot signal for GDScript interop or `Connect()` patterns.

```csharp
// Core — engine agnostic
public sealed class SteamMatchmaking
{
    public event Action<LobbyCreated> LobbyCreatedEvent;
}

// Godot layer — backed by the core event
public partial class SteamManager : Node
{
    [Signal] public delegate void LobbyCreatedEventHandler(long lobbyId, int result);

    private void OnCorelobbyCreated(LobbyCreated data)
    {
        EmitSignal(SignalName.LobbyCreated, (long)data.LobbyId, (int)data.Result);
    }
}
```

---

## 4. Lifecycle Model

### The Problem with Autoload-as-Canonical

An autoload is convenient, but it implies one global lifecycle that initializes on scene tree entry and shuts down on exit. This is too rigid for:

- Servers that need to initialize Steam before any scene loads
- Games with complex shutdown/restart behaviour (e.g. returning to a main menu and re-initializing)
- Unit test hosts that need Steam initialized without a running scene tree
- Headless dedicated server builds

### `SteamLifecycle` — The Core Initializer

The actual Steam initialization lives in `Manifold.Core` as a standalone class with no Godot dependency:

```csharp
// In Manifold.Core
public sealed class SteamLifecycle : IDisposable
{
    public static SteamLifecycle? Current { get; private set; }

    public bool IsInitialized { get; private set; }
    public uint AppId        { get; private set; }
    public SteamId LocalUser { get; private set; }

    // Explicit initialization — caller controls timing
    public static Result<SteamLifecycle> Initialize(SteamInitOptions options);

    // Must be called once per frame on the game thread
    public void RunCallbacks();

    // Graceful shutdown
    public void Dispose();
}

public sealed record SteamInitOptions
{
    public uint AppId          { get; init; }   // 0 = read steam_appid.txt
    public bool AllowRestart   { get; init; }   // true = relaunch through Steam if needed
    public bool ManualDispatch { get; init; }   // default true (recommended)
}
```

### `SteamManager` — The Godot Ergonomic Default

`SteamManager` is a Godot `Node` autoload that wraps `SteamLifecycle`. It is the easiest path for most games, but it is not the only path. Developers who need tighter control skip the autoload entirely and manage `SteamLifecycle` themselves.

```csharp
// In Manifold.Godot
public partial class SteamManager : Node
{
    // Access to the sub-interface wrappers (Godot-facing)
    public static SteamManager? Instance   { get; private set; }
    public SteamMatchmaking  Matchmaking   { get; private set; }   // Godot wrapper
    public SteamFriendsNode  Friends       { get; private set; }   // Godot wrapper
    public SteamUserNode     User          { get; private set; }   // Godot wrapper

    // Underlying core lifecycle — accessible if needed
    public SteamLifecycle? Lifecycle       { get; private set; }

    [Signal] public delegate void InitializedEventHandler();
    [Signal] public delegate void InitFailedEventHandler(string reason);
    [Signal] public delegate void ShutdownEventHandler();

    public override void _Ready()      { /* reads Project Settings, calls SteamLifecycle.Initialize */ }
    public override void _Process(double delta) { Lifecycle?.RunCallbacks(); }
    public override void _ExitTree()   { Lifecycle?.Dispose(); }
}
```

**Project Settings (optional — only relevant when using SteamManager):**
```
steam/app_id                  int   0 → reads steam_appid.txt
steam/auto_initialize         bool  true
steam/warn_if_not_running     bool  true
```

---

## 5. SDK & Platform Support

### Steamworks SDK Version
- **Primary target**: SDK 1.64 (present in project directory)
- **Upgrade path**: Re-run `ManifoldGen` against new SDK's `steam_api.json`

### Platform Support Matrix

| Platform | Native Binary | Status |
|---|---|---|
| Windows x64 | `steam_api64.dll` | ✅ Primary |
| Linux x64 | `libsteam_api.so` | ✅ Primary |
| macOS | `libsteam_api.dylib` | ✅ Primary |
| Windows x32 | `steam_api.dll` | ⚠️ Best-effort |

---

## 6. Interop Generator — Detailed Design

The generator (`tools/ManifoldGen/`) is a standalone C# console tool. It reads `steam_api.json` and emits `Manifold.Core.Interop` — the entire P/Invoke layer. Human beings never hand-edit generated files.

### Source of Truth

`sdk/public/steam/steam_api.json` contains:
- All interfaces and their methods (name, arguments, return types, calling convention)
- All structs with field names, types, and sizes
- All enums with values
- All callback types with their `k_iCallback` discriminant values
- All call result types

### Output Files (all under `Manifold.Core/Interop/Generated/`)

| File | Contents |
|---|---|
| `SteamNative.Methods.cs` | All `[LibraryImport]` P/Invoke declarations |
| `SteamNative.Structs.cs` | All `[StructLayout]` structs |
| `SteamNative.Enums.cs` | All C# enum types |
| `SteamNative.Callbacks.cs` | Callback struct definitions + `k_iCallback` constants |

All files carry a header comment: `// AUTO-GENERATED by ManifoldGen {version} from steam_api.json SDK {sdkVersion}. DO NOT EDIT.`

### Generator Policies

#### 1. Import Method — `[LibraryImport]` over `[DllImport]`

The generator emits `[LibraryImport]` (available since .NET 7) rather than `[DllImport]`. `[LibraryImport]` uses source-generated marshalling, produces better AOT/NativeAOT code, and gives the compiler visibility into marshalling at build time. This is Microsoft's recommended path for .NET 8+.

```csharp
// Emitted form
[LibraryImport(LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_CreateLobby")]
[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
internal static partial ulong CreateLobby(IntPtr self, ELobbyType eLobbyType, int cMaxMembers);
```

The library name is resolved per-platform using a `NativeLibrary.SetDllImportResolver` hook registered at startup — no per-call `#if` blocks.

#### 2. Bool Marshalling Policy — **Critical**

**The problem**: .NET's default bool marshalling maps `bool` → Windows `BOOL` (4 bytes, `int`). Steamworks uses C++ `bool` which is exactly **1 byte**. Passing a .NET `bool` with default marshalling to a function expecting a 1-byte bool will corrupt the stack on the callee side and produce silent, intermittent bugs.

**The policy**: Every `bool` return type and every `bool` parameter in a generated P/Invoke declaration **must** carry `[MarshalAs(UnmanagedType.U1)]` (or the `[LibraryImport]` source-gen equivalent `[return: MarshalAs(UnmanagedType.U1)]`).

```csharp
// WRONG — default marshalling, DO NOT emit this
[LibraryImport(...)]
internal static partial bool BLoggedOn(IntPtr self);

// CORRECT — explicit 1-byte marshalling
[LibraryImport(...)]
[return: MarshalAs(UnmanagedType.U1)]
internal static partial bool BLoggedOn(IntPtr self);

// CORRECT — bool parameter
[LibraryImport(...)]
internal static partial void SetRefuseNewConnections(
    IntPtr self,
    [MarshalAs(UnmanagedType.U1)] bool bRefuse);
```

The generator validates this: a post-generation lint pass asserts that every generated signature containing `bool` has an explicit `MarshalAs(U1)` annotation. A missing annotation is a generator bug, not a user bug.

#### 3. UTF-8 String Policy

`const char*` parameters → `[MarshalAs(UnmanagedType.LPUTF8Str)] string`. Output `char*` buffers (e.g. `GetPersonaName(char* pchName, int cchNameMax)`) → `byte[]` with the caller responsible for allocating the buffer and converting via `Encoding.UTF8.GetString(span)`. The generator emits a safe wrapper for the common "fill buffer, return string" pattern:

```csharp
// Raw P/Invoke (generated)
[LibraryImport(...)]
[return: MarshalAs(UnmanagedType.U1)]
internal static partial bool GetPersonaName_Raw(
    IntPtr self,
    byte[] pchName,
    int cchNameMax);

// Safe wrapper (also generated)
internal static string GetPersonaName(IntPtr self)
{
    Span<byte> buf = stackalloc byte[256];
    GetPersonaName_Raw(self, buf, buf.Length); // overload accepting Span<byte>
    return Encoding.UTF8.GetString(buf.TrimEnd((byte)0));
}
```

#### 4. Struct Packing & Alignment Policy

All generated structs use `[StructLayout(LayoutKind.Sequential, Pack = 1)]`. Steam structs are packed with no padding by convention (matching the Steamworks SDK's own layout assumptions). A generation-time validation step compares `Marshal.SizeOf<T>()` against the `"struct_size"` field in `steam_api.json` where available, and fails the build if they diverge.

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LobbyCreated_t
{
    internal const int k_iCallback = 513;
    internal EResult m_eResult;
    internal ulong   m_ulSteamIDLobby;
}
```

#### 5. Naming Normalization Policy

| SDK Name Form | Generated C# Name | Rule |
|---|---|---|
| `ISteamMatchmaking` | `SteamMatchmaking` (wrapper class) | Strip `I` prefix for public wrappers |
| `SteamAPI_ISteamMatchmaking_CreateLobby` | `CreateLobby` (P/Invoke method) | Strip `SteamAPI_ISteam*_` prefix |
| `k_ELobbyTypePublic` | `ELobbyType.Public` | Strip `k_E` prefix, use enum member |
| `m_ulSteamIDLobby` | field `m_ulSteamIDLobby` (internal struct) | Preserve SDK names in generated interop structs |
| `GetPersonaName` → public API | `GetPersonaName()` | PascalCase preserved for methods |
| `bRefuse` parameter | `refuse` (public API) | Strip Hungarian `b`/`n`/`ul` prefixes in public-facing wrappers |

Hungarian prefix stripping applies **only to public API wrapper signatures**, never to generated P/Invoke or internal struct fields (which preserve SDK names verbatim for debuggability).

#### 6. Reserved Keyword Handling

If a generated identifier clashes with a C# reserved keyword, prefix with `@`:

```csharp
// SDK has a parameter named "event"
internal static partial void PostEvent(IntPtr self, @event SteamNetworkingMessage_t msg);
```

The generator maintains a static set of all C# reserved and contextual keywords and checks all generated identifiers against it.

#### 7. Typedef Alias Policy

Steam typedefs that carry semantic meaning get dedicated C# structs rather than being inlined as primitives:

| SDK Typedef | C# Type | Rationale |
|---|---|---|
| `uint64_steamid` | `SteamId` (rich struct) | Domain type, see §9 |
| `HSteamNetConnection` | `NetConnection` (struct wrapping uint) | Prevents mixing connection handles |
| `HSteamListenSocket` | `ListenSocket` (struct wrapping uint) | Prevents mixing socket handles |
| `SteamAPICall_t` | `ulong` (internal only) | Used only inside `CallResultAwaiter<T>` |
| `HSteamPipe` | `uint` (internal only) | Never exposed publicly |

Raw primitive typedefs (`uint32`, `uint64`, etc.) map to their C# equivalents directly.

#### 8. Span / Buffer Handling for Fixed Arrays

Steam structs containing fixed-size arrays (e.g. `char m_szName[128]`) are emitted using `fixed` buffers in an `unsafe` context, paired with a safe accessor property:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct FriendGameInfo_t
{
    private fixed byte m_szName[128];

    internal string Name
    {
        get { fixed (byte* p = m_szName) return Encoding.UTF8.GetString(p, 128).TrimEnd('\0'); }
    }
}
```

The generator emits the `unsafe` field as private and generates the safe property accessor. Consumer code never uses `fixed` blocks directly.

#### 9. Unsupported Construct Policy

Some SDK constructs have no clean C# equivalent (e.g. union types, variadic args). The generator:

1. Emits the declaration as a `[Obsolete("Unsupported: <reason>")]` stub that throws `NotSupportedException`
2. Logs a warning to stdout during generation listing all skipped constructs
3. Records the skipped item in `ManifoldGen.skipped.json` for audit

This ensures the generated output always compiles; gaps are visible and tracked.

#### 10. SDK Diff & Upgrade Strategy

When a new SDK ships:

1. Run `ManifoldGen --diff old_sdk.json new_sdk.json` — produces a human-readable diff of added/removed/changed methods, structs, enums
2. Review the diff for breaking changes in the interfaces Manifold exposes
3. Run `ManifoldGen --generate new_sdk.json` — overwrites generated files only
4. Run the interop validation test suite (§10 below) — failures indicate ABI breaks
5. Update `Manifold.Core` public wrappers as needed
6. Bump version with `sdk{version}` suffix

Generated files are **never** committed with local modifications. If a generated declaration needs a fix, the fix goes into the generator itself or into the `ManifoldGen` policy, not into the generated file.

#### 11. Generated-Code Validation Tests

A dedicated test project (`Manifold.Interop.Tests`) runs at build time and verifies:

- Every generated struct's `Marshal.SizeOf<T>()` matches the expected size from the SDK schema
- Every generated `[LibraryImport]` declaration resolves against the actual `steam_api64.dll` (symbol exists)
- Every `bool` return/parameter has `MarshalAs(U1)` (via Roslyn analyzer, not runtime test)
- Every `k_iCallback` constant value matches the SDK JSON

---

## 7. Core Systems

### 7.1 `CallbackDispatcher`

Steam's flat API provides `SteamAPI_ManualDispatch_*` for explicit, deterministic callback control. `CallbackDispatcher` is the engine that drives all Steam events in Manifold:

```csharp
internal static class CallbackDispatcher
{
    // Maps k_iCallback discriminant → list of typed handlers
    private static readonly Dictionary<int, List<Action<IntPtr>>> _handlers = new();

    // Maps SteamAPICall_t handle → completion handler
    private static readonly Dictionary<ulong, Action<IntPtr, bool>> _callResults = new();

    internal static void Register(int callbackId, Action<IntPtr> handler)   { ... }
    internal static void Unregister(int callbackId, Action<IntPtr> handler) { ... }

    internal static void RegisterCallResult(ulong apiCall, Action<IntPtr, bool> completed) { ... }

    // Called once per frame by SteamLifecycle.RunCallbacks()
    internal static void Tick(HSteamPipe pipe)
    {
        SteamNative.SteamAPI_ManualDispatch_RunFrame(pipe);

        while (SteamNative.SteamAPI_ManualDispatch_GetNextCallback(pipe, out CallbackMsg_t msg))
        {
            try
            {
                if (_callResults.TryGetValue(msg.m_pubParam_apiCall, out var cr))
                {
                    bool ioFailed = SteamNative.SteamAPI_ManualDispatch_GetAPICallResult(...);
                    cr(msg.m_pubParam, ioFailed);
                    _callResults.Remove(msg.m_pubParam_apiCall);
                }
                else if (_handlers.TryGetValue(msg.m_iCallback, out var list))
                {
                    foreach (var h in list) h(msg.m_pubParam);
                }
            }
            finally
            {
                SteamNative.SteamAPI_ManualDispatch_FreeLastCallback(pipe);
            }
        }
    }
}
```

### 7.2 `CallResultAwaiter<T>`

Wraps a `SteamAPICall_t` handle into a `Task<T>` for async/await:

```csharp
internal static class CallResultAwaiter
{
    internal static Task<T> Await<T>(ulong apiCall) where T : unmanaged
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        CallbackDispatcher.RegisterCallResult(apiCall, (ptr, ioFailed) =>
        {
            if (ioFailed)
                tcs.SetException(new SteamIOFailedException(apiCall));
            else
                tcs.SetResult(Marshal.PtrToStructure<T>(ptr));
        });
        return tcs.Task;
    }
}

// Developer-facing usage
LobbyCreated result = await steam.Matchmaking.CreateLobbyAsync(ELobbyType.Public, maxMembers: 4);
```

### 7.3 `SteamId`

A strongly-typed, rich domain wrapper for Steam's 64-bit user/lobby/group IDs:

```csharp
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("{DebugDisplay,nq}")]
[JsonConverter(typeof(SteamIdJsonConverter))]
public readonly record struct SteamId : IEquatable<SteamId>, IComparable<SteamId>
{
    public readonly ulong Value;

    public SteamId(ulong value) => Value = value;

    public static readonly SteamId Invalid = new(0);
    public bool IsValid => Value != 0;

    // Comparison
    public int CompareTo(SteamId other) => Value.CompareTo(other.Value);
    public static bool operator <(SteamId a, SteamId b)  => a.Value < b.Value;
    public static bool operator >(SteamId a, SteamId b)  => a.Value > b.Value;
    public static bool operator <=(SteamId a, SteamId b) => a.Value <= b.Value;
    public static bool operator >=(SteamId a, SteamId b) => a.Value >= b.Value;

    // Conversion
    public static implicit operator ulong(SteamId id)     => id.Value;
    public static explicit operator SteamId(ulong value)  => new(value);

    // Parsing
    public static SteamId Parse(string s)
        => new(ulong.Parse(s, CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, out SteamId result)
    {
        if (ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out ulong v))
        {
            result = new(v);
            return true;
        }
        result = Invalid;
        return false;
    }

    // Display
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    private string DebugDisplay => IsValid ? $"SteamId({Value})" : "SteamId(Invalid)";

    // JSON — System.Text.Json converter (optional but included)
    // Serializes as string to avoid JavaScript number precision loss on 64-bit IDs
}
```

Similar strong-typed handle structs exist for `NetConnection` and `ListenSocket`.

---

## 8. Networking Layer

### 8.1 Transfer Mode → Steam Send Flag Mapping

Godot defines three transfer modes. Steam's `ISteamNetworkingSockets` has four send flags. The mapping is:

| Godot `TransferModeEnum` | Steam Send Flag | Notes |
|---|---|---|
| `Reliable` | `k_nSteamNetworkingSend_Reliable` (8) | Guaranteed delivery, ordered within the reliable stream |
| `UnreliableOrdered` | `k_nSteamNetworkingSend_Unreliable` (0) | **Semantic gap** — see below |
| `Unreliable` | `k_nSteamNetworkingSend_UnreliableNoDelay` (4) | No nagle, no buffering |

**The semantic gap**: Steam's `ISteamNetworkingSockets` has no native "unreliable ordered" mode. ENet's unreliable-ordered channel delivers packets in sequence (dropping out-of-order ones), but Steam's unreliable channel makes no ordering promise at all. Manifold maps `UnreliableOrdered` to plain `Unreliable` and documents this explicitly. If sequencing of unreliable packets is required, the application layer must implement its own sequence numbers. This is a deliberate, documented design decision — not a silent degradation.

The `NoNagle` peer flag (`NoNagle = true`) ORs `k_nSteamNetworkingSend_NoNagle` (1) into the send flags for all sends on that peer, overriding the defaults above.

### 8.2 Packet Size Guarantees

| Category | Limit | Source |
|---|---|---|
| Reliable message | ~1,048,576 bytes (1 MB) | `ISteamNetworkingSockets` documented maximum |
| Unreliable message | ~1,200 bytes | Practical MTU constraint; Steam may fragment but does not guarantee delivery of larger unreliable messages |
| Internal header overhead | 2 bytes (see §8.4) | Manifold-specific, transparent to caller |

`_GetMaxPacketSize()` returns `1_048_576`. The unreliable MTU constraint is documented but not enforced at the API level — callers sending large unreliable packets should expect silent drops.

### 8.3 Connection State Machine

The formal peer state machine for `SteamMultiplayerPeer`:

```
                       CreateHost()                CreateClient()
                           │                            │
                           ▼                            ▼
                      ┌─────────┐                ┌───────────┐
                      │Listening│                │Connecting │
                      └────┬────┘                └─────┬─────┘
                           │ (steam connection arrives) │ (Steam: Connected)
                           ▼                            ▼
                      ┌──────────────────────────────────────┐
                      │            Authenticating            │
                      │   (handshake packet exchange,        │
                      │    peer ID assignment in progress)   │
                      └────────────────┬─────────────────────┘
                                       │ (handshake complete)
                                       ▼
                      ┌──────────────────────────────────────┐
                      │              Connected               │
                      │  Normal operation. RPCs flowing.     │
                      └──┬─────────────────────────┬─────────┘
                         │ Close() called           │ Remote close / error
                         ▼                         ▼
                  ┌────────────┐           ┌────────────────────┐
                  │Disconnecting│           │ Disconnected       │
                  └──────┬──────┘           │ (emit PeerDiscon.) │
                         │                  └────────────────────┘
                         │ Steam confirms
                         ▼
                  ┌────────────────────┐
                  │   Disconnected     │
                  └────────────────────┘
```

**State definitions:**

| State | `_GetConnectionStatus()` | Description |
|---|---|---|
| `Listening` | `Connecting` | Host listening, no peers yet |
| `Connecting` | `Connecting` | Client initiated, waiting for Steam |
| `Authenticating` | `Connecting` | Steam connected, handshake in progress |
| `Connected` | `Connected` | Handshake complete, RPCs allowed |
| `Disconnecting` | `Connected` | Graceful close initiated, draining |
| `Disconnected` | `Disconnected` | Terminal state |

Transitions into `Disconnected` from any state are legal on Steam error or forced close.

### 8.4 Internal Packet Header

A 2-byte fixed header is prepended to every packet by Manifold, transparent to the caller. This replaces the original 1-byte proposal to give room for internal control packets without a future breaking change:

```
Byte 0 — Packet Kind
  0x00  Data packet (normal RPC/game traffic)
  0x01  Handshake — server→client peer ID assignment
  0x02  Handshake ACK — client confirms receipt
  0x03  Disconnect reason (graceful shutdown with code)
  0x04–0xFE  Reserved
  0xFF  Internal control (reserved for future use)

Byte 1 — Channel Index (0–255)
  Emulates ENet-style channels over a single Steam connection.
  Manifold strips this byte before passing data to Godot.
```

2 bytes of overhead per packet is negligible. The format is versioned implicitly — if we ever need to change it, the `Packet Kind` byte can carry a version flag in its upper nibble.

### 8.5 Disconnect Reason Surfacing

Steam provides `ESteamNetConnectionEnd` codes and a human-readable debug string. Manifold surfaces both:

```csharp
// In Manifold.Core
public readonly record struct DisconnectInfo(
    int    Code,            // ESteamNetConnectionEnd value
    string Reason,          // Human-readable string from Steam
    bool   WasLocalClose    // true = we closed it, false = remote or network
);

// Accessible via the peer after disconnection
public DisconnectInfo? LastDisconnectInfo { get; private set; }

// Also emitted as an event (core) and signal (Godot layer)
// Core:
public event Action<int, DisconnectInfo> PeerDisconnectedWithReason;
// Godot:
[Signal] public delegate void PeerDisconnectedWithReasonEventHandler(
    int godotPeerId, int code, string reason, bool wasLocal);
```

The standard `PeerDisconnected` signal is still emitted for Godot compatibility. `PeerDisconnectedWithReason` is an additive extension.

### 8.6 Peer ID Handshake Protocol

Steam IDs are 64-bit; Godot peer IDs are 32-bit. The server owns ID assignment:

1. Client establishes Steam connection → state enters `Authenticating`
2. Server assigns next available int ID, sends handshake packet:
   ```
   [0x01][channel=0][peer_id: int32 little-endian]  // 6 bytes total
   ```
3. Client receives handshake, stores `_uniqueId`, sends ACK:
   ```
   [0x02][channel=0]  // 2 bytes total
   ```
4. Server receives ACK → emits `PeerConnected(clientId)`, both sides enter `Connected`
5. Timeout: if handshake is not completed within 5 seconds, connection is closed with reason code `Timeout`

### 8.7 `SteamMultiplayerPeer` (Godot Layer)

```csharp
public partial class SteamMultiplayerPeer : MultiplayerPeerExtension
{
    // ── Setup ────────────────────────────────────────────────────────────────
    public Error CreateHost(int virtualPort = 0);
    public Error CreateClient(SteamId hostSteamId, int virtualPort = 0);
    public Task<Error> HostWithLobbyAsync(ELobbyType type, int maxMembers);
    public Task<Error> JoinLobbyAsync(SteamId lobbyId);

    // ── Config ───────────────────────────────────────────────────────────────
    public bool NoNagle    { get; set; } = false;
    public bool NoDelay    { get; set; } = false;
    public bool UseRelay   { get; set; } = true;

    // ── Disconnect info ───────────────────────────────────────────────────────
    public DisconnectInfo? LastDisconnectInfo { get; private set; }
    [Signal] public delegate void PeerDisconnectedWithReasonEventHandler(
        int peerId, int code, string reason, bool wasLocal);

    // ── MultiplayerPeerExtension — all 17 overrides implemented ─────────────
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
    public override bool  _IsServerRelaySupported();  // always true
    public override bool  _IsRefusingNewConnections();
    public override void  _SetRefuseNewConnections(bool pEnable);
    public override void  _Close();
    public override void  _DisconnectPeer(int pPeer, bool pForce);
}
```

### 8.8 `PeerIdMapper`

```csharp
internal sealed class PeerIdMapper
{
    private readonly Dictionary<SteamId, int> _steamToGodot = new();
    private readonly Dictionary<int, SteamId> _godotToSteam = new();
    private readonly Dictionary<uint, int>    _connToGodot  = new(); // HSteamNetConnection → godot id
    private int _nextId = 2; // server is always 1

    internal int  Register(SteamId steamId, uint connection);
    internal void Remove(int godotId);
    internal SteamId GetSteamId(int godotId);
    internal int     GetGodotId(SteamId steamId);
    internal int     GetGodotId(uint connection);
    internal bool    TryGetGodotId(uint connection, out int godotId);
}
```

---

## 9. API Wrapper Layer

Each interface wrapper (`SteamMatchmaking`, `SteamFriends`, etc.) lives in `Manifold.Core` and follows this contract:

- **Synchronous methods** for non-blocking API calls
- **`Task<T>` methods** for call results (suffixed `Async`)
- **C# `event Action<T>`** for recurring callbacks
- **Strongly typed returns** — no `Dictionary`, no raw `Variant`, no `long` where an enum belongs
- **XML doc comments** on every public member

The corresponding Godot-facing wrapper in `Manifold.Godot` adds `[Signal]` declarations backed by the core events, and exposes the same methods through a Godot `Node` or `RefCounted` that can be used from GDScript if desired.

---

## 10. Testing Strategy

### Philosophy

Coverage percentage is a floor, not a goal. The actual goal is **confidence that the public contract is correct, the interop is safe, and the networking state machine is sound**. Tests are organized by what they verify, not by what class they touch.

### Test Categories

#### 1. Contract Tests
Every public method and property on every public type in `Manifold.Core` and `Manifold.Godot` has at least one test that verifies its documented behaviour. These tests use `MockSteamBackend` (see below) and run without Steam installed.

**Coverage target**: 100% of public API surface (enforced by a test that enumerates public members via reflection and cross-references against the test suite).

#### 2. Interop Verification Tests (`Manifold.Interop.Tests`)
Run at build time against the actual native library:

- Every generated struct's `Marshal.SizeOf<T>()` matches the SDK schema
- Every `[LibraryImport]` entry point resolves in `steam_api64.dll`
- Every `bool` P/Invoke declaration has `MarshalAs(U1)` (Roslyn analyzer)
- Every `k_iCallback` constant matches the SDK JSON
- Platform smoke tests: library loads and `SteamAPI_InitFlat` returns on Windows, Linux, macOS

These tests require the native DLL to be present but **do not require Steam to be running**.

#### 3. State Machine / Protocol Tests
Tests that drive the `SteamMultiplayerPeer` and `PeerIdMapper` through every defined state transition using the mock backend:

- `Disconnected → Connecting → Authenticating → Connected` (host and client paths)
- Handshake timeout → `Disconnected` with reason code
- `Connected → Disconnecting → Disconnected` (local close)
- Remote close from `Connected` → `Disconnected` with disconnect info populated
- Negative target peer broadcast (all-except routing)
- Channel stripping: packet received with 2-byte header → Godot sees raw data only
- All 17 `MultiplayerPeerExtension` override contracts

#### 4. Callback & Async Tests
- `CallbackDispatcher` routing: correct handler called for each `k_iCallback`
- `CallResultAwaiter<T>` resolves on success, throws `SteamIOFailedException` on `ioFailed`
- `CallResultAwaiter<T>` does not leak `TaskCompletionSource` if call result never arrives (timeout path)
- Thread safety: `RunCallbacks()` and result handlers always on the same thread (asserted)

#### 5. Godot Integration Tests (GdUnit4, headless)
- `SteamMultiplayerPeer` instantiates in a headless Godot process
- `Multiplayer.MultiplayerPeer = peer` accepted without error
- `@rpc` call round-trips between two peers in a single-process loopback test
- `SteamManager` autoload initializes and exposes sub-interfaces correctly
- `[Signal]` fires when core `event` fires

#### 6. Platform Smoke Tests (CI)
Run on Windows, Linux, and macOS via GitHub Actions. Verify:
- Native library loads (using `SteamAPI_InitFlat` offline path — no Steam required)
- Basic P/Invoke call succeeds without segfault or `DllNotFoundException`
- Package build produces valid `.nupkg` with correct `runtimes/` structure

### Mock Backend

```csharp
// Manifold.Core.Testing
public interface ISteamNativeBackend
{
    bool    BLoggedOn(IntPtr self);
    ulong   CreateLobby(IntPtr self, ELobbyType type, int max);
    // ... one method per P/Invoke call used by the core
}

// Production impl — forwards to SteamNative P/Invoke
internal sealed class LiveSteamBackend : ISteamNativeBackend { ... }

// Test impl — returns controllable data, records calls
public sealed class MockSteamBackend : ISteamNativeBackend
{
    public bool IsLoggedOn { get; set; } = true;
    public List<string> CallLog { get; } = new();
    // Configurable return values per method
}
```

The `ISteamNativeBackend` interface is injected into all core wrappers, making every unit test deterministic, fast, and Steam-free.

---

## 11. Distribution & Packaging

### NuGet Package: `Manifold.Godot`

```
Manifold.Godot.nupkg
├── lib/net8.0/
│   └── Manifold.dll
├── runtimes/
│   ├── win-x64/native/steam_api64.dll
│   ├── linux-x64/native/libsteam_api.so
│   └── osx/native/libsteam_api.dylib
├── build/
│   └── Manifold.Godot.targets   ← MSBuild: copy natives to output dir automatically
└── README.md
```

`Manifold.Core` is a separate, lighter package with no native binaries for use in headless/server contexts where the developer manages the native library themselves.

### MSBuild Targets

The `.targets` file ensures `steam_api64.dll` (or platform equivalent) is copied to the build output directory automatically on every build. Developers install the NuGet package and it works — no manual file copying, no `steam_appid.txt` reminder needed beyond the docs.

### Versioning

`MAJOR.MINOR.PATCH-sdk{SDK_VERSION}`
Example: `1.0.0-sdk164`

---

## 12. Project Structure

```
Project Steamworks/
├── MASTER_DESIGN.md
├── MASTER_DESIGN_v0.1_backup.md
├── steamworks_sdk_164/
├── src/
│   ├── Manifold.Core/
│   │   ├── Manifold.Core.csproj
│   │   ├── Core/
│   │   │   ├── SteamLifecycle.cs
│   │   │   ├── SteamInitOptions.cs
│   │   │   ├── CallbackDispatcher.cs
│   │   │   ├── CallResultAwaiter.cs
│   │   │   ├── SteamId.cs
│   │   │   ├── NetConnection.cs
│   │   │   ├── ListenSocket.cs
│   │   │   ├── DisconnectInfo.cs
│   │   │   └── SteamException.cs
│   │   ├── Networking/
│   │   │   ├── SteamNetworkingCore.cs
│   │   │   ├── PeerIdMapper.cs
│   │   │   ├── HandshakeProtocol.cs
│   │   │   └── PacketHeader.cs
│   │   ├── Matchmaking/
│   │   │   ├── SteamMatchmaking.cs
│   │   │   └── Models/
│   │   ├── Friends/
│   │   │   ├── SteamFriends.cs
│   │   │   └── Models/
│   │   ├── User/
│   │   │   ├── SteamUser.cs
│   │   │   └── Models/
│   │   ├── Utils/
│   │   │   └── SteamUtils.cs
│   │   ├── Testing/
│   │   │   ├── ISteamNativeBackend.cs
│   │   │   └── MockSteamBackend.cs
│   │   └── Interop/Generated/      ← DO NOT EDIT — ManifoldGen output
│   │       ├── SteamNative.Methods.cs
│   │       ├── SteamNative.Structs.cs
│   │       ├── SteamNative.Enums.cs
│   │       └── SteamNative.Callbacks.cs
│   ├── Manifold.Godot/
│   │   ├── Manifold.Godot.csproj
│   │   ├── SteamManager.cs
│   │   ├── SteamSignalBridge.cs
│   │   ├── Networking/
│   │   │   ├── SteamMultiplayerPeer.cs
│   │   │   └── SteamLobbySession.cs
│   │   ├── Matchmaking/
│   │   │   └── SteamMatchmakingNode.cs
│   │   └── Friends/
│   │       └── SteamFriendsNode.cs
│   ├── Manifold.Core.Tests/
│   │   ├── Contract/
│   │   ├── StateMachine/
│   │   ├── Callbacks/
│   │   └── Interop/
│   └── Manifold.Godot.Tests/
│       └── (GdUnit4 C# tests)
├── tools/
│   └── ManifoldGen/
│       ├── ManifoldGen.csproj
│       ├── Program.cs
│       ├── JsonModels.cs
│       ├── TypeMapper.cs
│       ├── PolicyValidator.cs
│       └── Emitters/
│           ├── MethodEmitter.cs
│           ├── StructEmitter.cs
│           ├── EnumEmitter.cs
│           └── CallbackEmitter.cs
├── samples/
│   └── ManifoldSample/
└── docs/
    ├── getting-started.md
    ├── lifecycle.md
    ├── multiplayer-peer.md
    ├── transfer-modes.md
    ├── callbacks-and-async.md
    └── api/
```

---

## 13. Development Phases

### Phase 1 — Foundation
- [ ] Solution and project structure
- [ ] `ManifoldGen` tool — reads `steam_api.json`, emits full `Manifold.Core.Interop`
- [ ] `ManifoldGen` policy validator (bool check, struct size check, symbol check)
- [ ] `SteamId`, `NetConnection`, `ListenSocket`, `DisconnectInfo` types
- [ ] `SteamLifecycle` — init/shutdown/RunCallbacks, no Godot dependency
- [ ] `CallbackDispatcher` — manual dispatch, recurring callbacks, call results
- [ ] `CallResultAwaiter<T>`
- [ ] `SteamException` hierarchy
- [ ] `ISteamNativeBackend` + `MockSteamBackend`
- [ ] Contract + interop + callback tests for all Phase 1 components
- [ ] Verified P/Invoke round-trip: init → get local steam ID → shutdown

### Phase 2 — Networking Core
- [ ] `PacketHeader` (2-byte format, encode/decode)
- [ ] `HandshakeProtocol` (state machine, timeout handling)
- [ ] `PeerIdMapper`
- [ ] `SteamNetworkingCore` (ISteamNetworkingSockets wrapper, no Godot types)
- [ ] `SteamMultiplayerPeer` (all 17 overrides, state machine, disconnect info)
- [ ] `SteamLobbySession`
- [ ] State machine, protocol, and Godot integration tests
- [ ] End-to-end: two peers connect via loopback, exchange data, disconnect cleanly

### Phase 3 — API Surface
- [ ] `SteamUser`
- [ ] `SteamFriends`
- [ ] `SteamMatchmaking`
- [ ] `SteamUtils`
- [ ] Corresponding Godot-layer nodes + signal bridges
- [ ] `SteamManager` autoload
- [ ] Contract tests for all new surface

### Phase 4 — Polish & Distribution
- [ ] NuGet packaging (`Manifold.Core` + `Manifold.Godot`) with native bundling
- [ ] MSBuild `.targets` for automatic native copy
- [ ] Sample Godot project (lobby browser + 2-player RPC demo)
- [ ] Full documentation
- [ ] GitHub Actions CI: build + test on Windows/Linux/macOS
- [ ] Platform smoke tests in CI

---

## 14. Key Decisions & Rationale

| Decision | Choice | Rationale |
|---|---|---|
| Split packages | `Manifold.Core` + `Manifold.Godot` | Engine-agnostic core enables cheap unit testing, server builds, future engine ports |
| Lifecycle model | `SteamLifecycle` (core) + `SteamManager` (ergonomic default) | Developers who need control bypass the autoload; most developers never think about it |
| Import method | `[LibraryImport]` | .NET 8 recommended path; source-gen marshalling; better AOT support than `[DllImport]` |
| Bool marshalling | `[MarshalAs(UnmanagedType.U1)]` always | Steam uses C++ `bool` = 1 byte; .NET default marshals to 4-byte BOOL; mismatch = silent stack corruption |
| Callback dispatch | `SteamAPI_ManualDispatch_*` | Deterministic, frame-aligned, main-thread-safe |
| Call results | `Task<T>` | Modern C#; composable; no callback registration boilerplate for callers |
| Events vs Signals | Core uses `event`; Godot layer uses `[Signal]` backed by `event` | Clear separation; C# code uses typed events; GDScript uses signals |
| Unreliable-ordered | Maps to Unreliable (documented gap) | Steam has no native ordered-unreliable; honest mapping beats silent semantic change |
| Packet header | 2-byte (kind + channel) | Room for internal control packets without future format break; negligible overhead |
| Channel emulation | 1-byte channel index in header | Avoids multiple listen sockets; transparent to caller |
| SteamId | Rich record struct | Prevents ID/primitive confusion; parse/compare/JSON support; debugger display |
| Mock backend | `ISteamNativeBackend` interface | Every unit test is Steam-free, deterministic, and fast |
| Relay topology | `_IsServerRelaySupported()` = true | Matches Steam's relay model; simplest NAT traversal |

---

## 15. Open Questions

- [ ] **Library name final?** — `Manifold` pending confirmation
- [ ] **Game server support (`ISteamGameServer`)?** — Defer to Phase 5?
- [ ] **GDScript bridge?** — Should Godot-layer types carry `[GlobalClass]`? Or C#-only? Given the engine-agnostic core split, a thin GDScript-accessible Godot layer is possible at low cost.
- [ ] **Editor plugin?** — Validate `steam_appid.txt` presence, show Steam init status in editor. Low effort, high DX value.
- [ ] **Facepunch.Steamworks coexistence?** — Document incompatibility (both will call `SteamAPI_Init`) or provide guidance.
- [ ] **`UnreliableOrdered` gap** — Accept the documented mapping, or implement application-layer sequencing inside Manifold? (Recommendation: accept and document for v1, revisit in v2 based on user feedback.)

---

*This document is the canonical reference for Manifold's design. All implementation decisions must be reconciled against it. Update this document before implementing any significant architectural change.*
