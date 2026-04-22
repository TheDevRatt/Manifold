# Manifold — Master Design Document
**A Godot-First C# Steamworks SDK Library**

> Version: 0.5
> Author: Matthew Makary
> Last Updated: 2026-04-21
> Status: Pre-Development / Architecture Phase
> Previous Versions: MASTER_DESIGN_v0.1_backup.md, MASTER_DESIGN_v0.2_backup.md, MASTER_DESIGN_v0.3_backup.md, MASTER_DESIGN_v0.4_backup.md

---

## 1. Project Vision

Manifold is a C# library that exposes the Steamworks SDK to Godot 4 game developers. It is split into two layers: a **Godot-agnostic core** (`Manifold.Core`) and a **Godot integration layer** (`Manifold.Godot`). The core can be used, tested, and reasoned about without any dependency on Godot types. The Godot layer wires the core into Godot's multiplayer system, node lifecycle, and project settings.

It is not a wrapper around GodotSteam, not a port of Steamworks.NET, and not a generated binding dump. It is strictly C# — no GDScript exposure — targeting developers who work in Godot's C# environment.

### Core Principles

- **Engine-agnostic core**: `Manifold.Core` has zero Godot dependency. Steam API wrappers, the P/Invoke layer, callback dispatch, and type definitions live here. Unit testing is fast, cheap, and Steam-free.
- **Godot-first integration**: `Manifold.Godot` wires the core into Godot conventions — `MultiplayerPeerExtension`, `[Signal]`, node lifecycle, `ProjectSettings`. Godot types never leak into the core.
- **No intermediary**: P/Invoke directly against `steam_api64.dll` / `libsteam_api.so` via Valve's flat C API (`steam_api_flat.h`). No GodotSteam, no Steamworks.NET.
- **Modern C#**: `async`/`await` with `CancellationToken` for call results, C# `event` in the core layer, Godot `[Signal]` in the Godot layer. Nullable reference types. Records for Steam data structures.
- **Lifecycle flexibility**: `SteamLifecycle` is the core initializer. `SteamManager` is the Godot ergonomic default — not the canonical model.
- **Strictly C#**: No GDScript exposure. Manifold does not step on GodotSteam's territory.
- **100% meaningful coverage**: Coverage is contract-oriented. Every public API surface, interop declaration, and state machine transition is verified. Numbers are a floor, not a ceiling.
- **Best developer experience**: Bundled native binaries, IntelliSense-complete XML docs on every public member, NuGet distribution, clear error messages, zero manual wiring.

---

## 2. Name & Identity

| Property | Value |
|---|---|
| Library Name | **Manifold** |
| NuGet Package (core) | `Manifold.Core` |
| NuGet Package (Godot) | `Manifold.Godot` |
| Root Namespace | `Manifold` |
| Target SDK | Steamworks SDK 1.64 |
| Target Engine | Godot 4.3+ (.NET / C#) |
| .NET Target | net8.0 |
| License | MIT |

---

## 3. Package Split & Dependency Graph

```
Manifold.Core                   ← No Godot dependency. Pure .NET.
├── Interop/Generated/          ← Generated P/Invoke layer (ManifoldGen output)
├── Core/                       ← SteamLifecycle, CallbackDispatcher, types
├── Networking/                 ← Steam networking logic, no Godot types (INTERNAL)
├── Matchmaking/                ← ISteamMatchmaking wrappers
├── Friends/                    ← ISteamFriends wrappers
├── User/                       ← ISteamUser wrappers
├── Utils/                      ← ISteamUtils, ISteamNetworkingUtils
├── Testing/                    ← Capability backend interfaces (IMatchmakingBackend,
│                                  INetworkingBackend, IUserBackend) + FakeSteamBackend

Manifold.Godot                  ← Depends on Manifold.Core + GodotSharp
├── SteamMultiplayerPeer        ← MultiplayerPeerExtension impl
├── SteamLobbySession           ← Godot RefCounted, session lifecycle
├── SteamManager                ← Godot Node autoload (ergonomic default)
├── SteamSignalBridge           ← Core C# events → Godot [Signal]
└── Nodes/                      ← Godot-facing wrappers (SteamMatchmakingNode, etc.)
```

### Event vs Signal Rule

**Core layer (`Manifold.Core`):** Callbacks are exposed as C# `event Action<T>`. No Godot types.

**Godot layer (`Manifold.Godot`):** Godot-facing components expose Godot `[Signal]` declarations. Internally they subscribe to the corresponding core `event` and re-emit as a Godot signal. Both C# events and Godot signals are always available from the Godot layer — use the event for typed C# code, the signal for `Connect()` patterns.

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

    private void OnCoreLobbyCreated(LobbyCreated data)
        => EmitSignal(SignalName.LobbyCreated, (long)data.LobbyId, (int)data.Result);
}
```

---

## 4. Lifecycle Model

### `SteamLifecycle` — The Core Initializer (in `Manifold.Core`)

`SteamLifecycle` is the single source of truth for Steam's initialized state. It has no Godot dependency and can be used in headless servers, unit test hosts, or any custom initialization context.

```csharp
/// <summary>
/// Minimal discriminated result type used for operations that can fail
/// without throwing — specifically SteamLifecycle.Initialize(), which must
/// not throw because callers need to branch on init failure without try/catch.
/// Not a general-purpose algebraic type. Keep it small.
/// </summary>
public readonly struct Result<T>
{
    public bool   IsSuccess { get; }
    public T?     Value     { get; }
    public string Error     { get; }  // human-readable; never null on failure

    private Result(T value)           { IsSuccess = true;  Value = value; Error = string.Empty; }
    private Result(string error)      { IsSuccess = false; Value = default; Error = error; }

    public static Result<T> Ok(T value)      => new(value);
    public static Result<T> Fail(string msg) => new(msg);

    // Pattern-match friendly.
    // On success: value is the real result, error is empty string.
    // On failure: value is default! — callers must not use it; error carries the message.
    public bool TryGetValue(out T value, out string error)
    {
        if (IsSuccess)
        {
            value = Value!;        // guaranteed non-null on the success path
            error = string.Empty;
        }
        else
        {
            value = default!;      // sentinel — caller must ignore this on failure
            error = Error;
        }
        return IsSuccess;
    }
}

public sealed class SteamLifecycle : IDisposable
{
    public static SteamLifecycle? Current { get; private set; }

    public bool    IsInitialized { get; private set; }
    public uint    AppId         { get; private set; }
    public SteamId LocalUser     { get; private set; }

    /// <summary>
    /// Initializes Steam. May only be called once per process lifetime.
    /// Returns Result.Fail if Steam is already initialized, if Steam is not running,
    /// or if SteamAPI_Init returns a failure code.
    /// Calling Initialize() a second time — even after Dispose() — returns Result.Fail.
    ///
    /// Captures Thread.CurrentThread.ManagedThreadId at call time as the authoritative
    /// "game thread" ID. CallbackDispatcher.DebugAssertMainThread() uses this value
    /// to assert that RunCallbacks() is never called from a background Task or Thread.
    /// Initialize() must therefore always be called from the game/main thread.
    /// </summary>
    public static Result<SteamLifecycle> Initialize(SteamInitOptions options);

    // Must be called once per frame on the same thread that called Initialize().
    // In debug builds, asserts that the calling thread matches the captured game thread ID.
    public void RunCallbacks();

    public void Dispose();
}

public sealed record SteamInitOptions
{
    public uint AppId          { get; init; }  // 0 = read steam_appid.txt
    public bool AllowRestart   { get; init; }  // relaunch through Steam if not started via Steam
    public bool ManualDispatch { get; init; } = true;
    public TimeSpan CallResultTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
```

### `SteamLifecycle` Initialization State Machine

`SteamLifecycle` follows a strict one-way state machine. There is no re-initialization path.

```
Uninitialized ──── Initialize() OK ────► Initialized ──── Dispose() ────► Disposed
      │                                                                        │
      └──── Initialize() called again ─────────────────────────────────────────┘
            → Result.Fail("SteamLifecycle is already initialized.")
            (returns immediately, no native calls made)

Initialized ──── Initialize() called again
            → Result.Fail("SteamLifecycle is already initialized.")

Disposed ──── Initialize() called again
            → Result.Fail("SteamLifecycle has already been disposed and cannot be re-initialized.")
```

**Rationale for strict failure on second call**: Returning `Result.Ok(Current)` on a second `Initialize()` call would silently hide a programming error. The caller may have passed different `SteamInitOptions` — silently ignoring them is worse than surfacing the mistake. If a developer calls `Initialize` twice, they have a bug. They should be told.

**`SteamLifecycle.Current` lifetime**:
- `null` before first successful `Initialize()`
- Non-null after successful `Initialize()`
- Set back to `null` at step 6 of `Dispose()` (after `SteamAPI_Shutdown()` completes)
- Remains `null` permanently after disposal — no re-initialization

> **Implementation note — dual null states**: `Current == null` is ambiguous by design. It can mean "never initialized" or "initialized then disposed." These are deliberately not distinguished by `Current` alone — the internal `_disposed` flag is what separates them. This is why `_disposed` must be checked first in all guarded paths (`Initialize()`, `RunCallbacks()`, public API calls). Do not infer lifecycle state from `Current == null` alone in implementation code.

### Shutdown Contract (`SteamLifecycle.Dispose()`)

Dispose follows this sequence, in order, with no deviation:

1. Set `_disposed = true` immediately — all subsequent public method calls throw `ObjectDisposedException`
2. Cancel all pending `CallResultAwaiter<T>` completions by faulting their `TaskCompletionSource` with `SteamShutdownException`
3. Transition all active `SteamMultiplayerPeer` instances to `Disconnected` state, emitting `PeerDisconnected` for each connected peer before any native shutdown occurs
4. Clear all `CallbackDispatcher` handler registrations
5. Call `SteamAPI_Shutdown()` on the native layer
6. Set `SteamLifecycle.Current = null`

**Dispose is idempotent.** Calling it a second time is a silent no-op — the `_disposed` flag check at step 1 short-circuits everything.

### `SteamManager` — The Godot Ergonomic Default (in `Manifold.Godot`)

A Godot `Node` autoload that wraps `SteamLifecycle`. The default path for most games. Developers who need initialization timing control, custom shutdown sequences, or headless server builds bypass it entirely and manage `SteamLifecycle` directly.

```csharp
public partial class SteamManager : Node
{
    public static SteamManager? Instance { get; private set; }

    // Underlying lifecycle — accessible for advanced use
    public SteamLifecycle? Lifecycle { get; private set; }

    // Sub-interface access (Godot-facing wrappers)
    public SteamMatchmakingNode Matchmaking { get; private set; }
    public SteamFriendsNode     Friends     { get; private set; }
    public SteamUserNode        User        { get; private set; }

    [Signal] public delegate void InitializedEventHandler();
    [Signal] public delegate void InitFailedEventHandler(string reason);
    [Signal] public delegate void ShutdownEventHandler();

    public override void _Ready()
    {
        // Reads AppID from Project Settings (steam/app_id) or steam_appid.txt if unset.
        // Logs the AppID and its source explicitly:
        //   [Manifold] Initialized with AppID 480 (source: Project Settings 'steam/app_id')
        //   [Manifold] Initialized with AppID 480 (source: steam_appid.txt)
        // This prevents silent conflicts when both sources are present with different values.
    }
    public override void _Process(double _) { Lifecycle?.RunCallbacks(); }
    public override void _ExitTree() { Lifecycle?.Dispose(); }
}
```

**Project Settings (only relevant when using SteamManager):**
```
steam/app_id                int   0 → reads steam_appid.txt
steam/auto_initialize       bool  true
steam/warn_if_not_running   bool  true
steam/call_result_timeout   int   30  (seconds)
```

---

## 5. SDK & Platform Support

### Steamworks SDK Version
- **Primary target**: SDK 1.64
- **Upgrade path**: Re-run `ManifoldGen --generate new_sdk.json` (see §6)

### Platform Support Matrix

| Platform | Native Binary | Callback Pack | Status |
|---|---|---|---|
| Windows x64 | `steam_api64.dll` | `Pack=8` (`VALVE_CALLBACK_PACK_LARGE`) | ✅ Primary |
| Linux x64 | `libsteam_api.so` | `Pack=4` (`VALVE_CALLBACK_PACK_SMALL`) | ✅ Primary |
| macOS | `libsteam_api.dylib` | `Pack=4` (`VALVE_CALLBACK_PACK_SMALL`) | ✅ Primary |
| Windows x32 | `steam_api.dll` | `Pack=8` | ⚠️ Best-effort |

---

## 6. Interop Generator — Detailed Design

The generator (`tools/ManifoldGen/`) is a standalone C# console tool. It reads `steam_api.json` and the SDK headers, and emits `Manifold.Core/Interop/Generated/`. Human beings never hand-edit generated files.

### Source of Truth

- `steam_api.json` — methods, args, return types, enums, typedefs, callback IDs
- Header files — `#pragma pack` directives, which determine per-struct `Pack` values (the JSON has no struct size fields)

### Output Files

| File | Contents |
|---|---|
| `SteamNative.Methods.cs` | All `[LibraryImport]` P/Invoke declarations |
| `SteamNative.Structs.cs` | All `[StructLayout]` structs with correct per-struct `Pack` |
| `SteamNative.Enums.cs` | All C# enum types |
| `SteamNative.Callbacks.cs` | Callback struct definitions + `k_iCallback` constants |

All files carry: `// AUTO-GENERATED by ManifoldGen {version} from steam_api.json SDK {sdkVersion}. DO NOT EDIT.`

### Generator Policies

#### 1. Import Method — `[LibraryImport]`

The generator emits `[LibraryImport]` (available since .NET 7, recommended for .NET 8+) rather than `[DllImport]`. `[LibraryImport]` uses source-generated marshalling, improves AOT/NativeAOT compatibility, and gives the compiler visibility into marshalling at build time.

Platform-conditional library name resolution uses `NativeLibrary.SetDllImportResolver` registered at `SteamLifecycle.Initialize()` — no per-call `#if` blocks.

```csharp
[LibraryImport(LibraryName, EntryPoint = "SteamAPI_ISteamMatchmaking_CreateLobby")]
[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
internal static partial ulong CreateLobby(IntPtr self, ELobbyType eLobbyType, int cMaxMembers);
```

#### 2. Bool Marshalling Policy — **Non-Negotiable**

Steam uses C++ `bool` which is **exactly 1 byte**. .NET's default bool marshalling maps to Windows `BOOL` (4 bytes). This mismatch causes silent stack corruption that may only occasionally produce the wrong result — one of the worst bug categories.

**Rule**: Every `bool` return type and every `bool` parameter in every generated P/Invoke declaration **must** carry explicit `[MarshalAs(UnmanagedType.U1)]`.

```csharp
// WRONG — never emit this
[LibraryImport(...)]
internal static partial bool BLoggedOn(IntPtr self);

// CORRECT
[LibraryImport(...)]
[return: MarshalAs(UnmanagedType.U1)]
internal static partial bool BLoggedOn(IntPtr self);

// CORRECT — parameter
[LibraryImport(...)]
internal static partial void SetRefuseNewConnections(
    IntPtr self,
    [MarshalAs(UnmanagedType.U1)] bool bRefuse);
```

A Roslyn analyzer runs at build time and fails the build if any generated P/Invoke declaration contains `bool` without `MarshalAs(U1)`. A missing annotation is a **generator bug**, caught at build time, never at runtime.

#### 3. Struct Packing Policy — Per-Struct, Header-Derived

> **Infrastructure note**: Validating struct sizes against the native ABI requires a thin C helper that exposes `sizeof(StructName)` for each callback struct. This is not a pure C# library concern — it introduces a native compilation step (C compiler, CMake or equivalent), platform toolchains for all three targets, and a native CI pipeline that must be in place **before** the C# interop validation tests can run. This is an acknowledged first-class infrastructure cost. The native helper and its CI pipeline must be built and verified on Windows x64, Linux x64, and macOS before Phase 1 interop tests are considered passing. Do not treat this as a footnote.

**Background**: The SDK uses a platform-conditional packing system for callback structs, controlled by `VALVE_CALLBACK_PACK_SMALL` / `VALVE_CALLBACK_PACK_LARGE`:

- `VALVE_CALLBACK_PACK_SMALL` (`Pack=4`): Linux, macOS, FreeBSD
- `VALVE_CALLBACK_PACK_LARGE` (`Pack=8`): Windows

A handful of specific structs (`SteamNetworkingMessage_t`, some input structs, `CSteamID` internal union) use explicit `Pack=1`. These are individually identified in the headers via `#pragma pack(push, 1)`.

**Policy**:
1. The generator parses `#pragma pack` directives from the SDK headers to determine which pack value applies to each struct definition
2. Each generated struct carries the exact `Pack` value derived from its enclosing pragma block — not a blanket global value
3. The default assumption for structs not within any pragma block is `LayoutKind.Sequential` with no explicit `Pack` (platform default alignment), which is correct for non-callback structs
4. **Validation**: At test time, the interop test suite loads the native DLL and uses a `SizeOf` validation routine that calls into a thin C helper that exposes `sizeof(StructName)` for each callback struct. This is the authoritative size check. If `Marshal.SizeOf<T>()` on the current platform doesn't match the native sizeof, the test fails and generation is blocked.

```csharp
// Example: callback struct under PACK_SMALL (Linux/macOS)
#if MANIFOLD_PACK_SMALL
[StructLayout(LayoutKind.Sequential, Pack = 4)]
#else
[StructLayout(LayoutKind.Sequential, Pack = 8)]
#endif
internal struct LobbyCreated_t
{
    internal const int k_iCallback = 513;
    internal EResult m_eResult;
    internal ulong   m_ulSteamIDLobby;
}

// Example: explicitly Pack=1 struct
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SteamNetworkingMessage_t { ... }
```

The generator emits `#if MANIFOLD_PACK_SMALL / #else / #endif` blocks for the platform-conditional structs. The `MANIFOLD_PACK_SMALL` conditional compilation symbol is defined in the `.csproj` based on the build target RID.

> **Build plumbing note**: The symbol approach is conceptually correct but requires airtight CI coverage to be trustworthy. If `MANIFOLD_PACK_SMALL` is absent or misconfigured on a given platform, the wrong `Pack` value is silently applied — the compiler produces no error. The primary safety net is the interop size validation tests (§6.12), which load the native DLL and compare `Marshal.SizeOf<T>()` against native `sizeof(T)` at runtime on every platform in the CI matrix. If the symbol is wrong, the size test fails loudly. The CI matrix must build and run these tests on all three target platforms (Windows x64, Linux x64, macOS), never only on the development machine.

#### 4. UTF-8 String Policy

`const char*` parameters → `[MarshalAs(UnmanagedType.LPUTF8Str)] string`

Output `char*` buffers (caller-allocated) → `byte[]` or `Span<byte>` with a generated safe wrapper that handles the allocation and `Encoding.UTF8.GetString` conversion. Consumer code never deals with `char*` pointers directly.

```csharp
// Raw (generated)
// [LibraryImport] source generators support Span<byte> parameters directly —
// the runtime pins the span and passes the pointer to the native function,
// so the native write lands in the original buffer, not a copy.
[LibraryImport(...)]
[return: MarshalAs(UnmanagedType.U1)]
internal static partial bool GetPersonaName_Raw(IntPtr self, Span<byte> pchName, int cchNameMax);

// Safe wrapper (also generated)
internal static string GetPersonaName(IntPtr self)
{
    Span<byte> buf = stackalloc byte[256];
    GetPersonaName_Raw(self, buf, buf.Length);   // native writes into buf directly
    int end = buf.IndexOf((byte)0);
    return Encoding.UTF8.GetString(end >= 0 ? buf[..end] : buf);
}
```

#### 5. Naming Normalization Policy

| SDK Form | C# Form | Rule |
|---|---|---|
| `ISteamMatchmaking` | `SteamMatchmaking` (wrapper class) | Strip `I` prefix on public wrappers |
| `SteamAPI_ISteamMatchmaking_CreateLobby` | `CreateLobby` (P/Invoke method) | Strip `SteamAPI_ISteam*_` prefix |
| `k_ELobbyTypePublic` | `ELobbyType.Public` | Strip `k_E` prefix, use enum member |
| `m_ulSteamIDLobby` | field `m_ulSteamIDLobby` | Preserve SDK names in generated interop structs verbatim |
| `bRefuse` parameter (public API) | `refuse` | Strip Hungarian prefixes (`b`, `n`, `ul`, `psz`) in public wrappers only |

Hungarian prefix stripping applies **only to public-facing wrapper signatures**, never to generated interop structs or internal P/Invoke declarations (verbatim SDK names for debuggability).

#### 6. Reserved Keyword Handling

Generated identifiers that clash with C# reserved or contextual keywords are prefixed with `@`:

```csharp
internal static partial void PostEvent(IntPtr self, @event SteamNetworkingMessage_t msg);
```

The generator maintains a static set of all C# reserved and contextual keywords and checks every generated identifier.

#### 7. Typedef Alias Policy

| SDK Typedef | C# Type | Rationale |
|---|---|---|
| `uint64_steamid` | `SteamId` (rich struct) | Domain type — see §8 |
| `HSteamNetConnection` | `NetConnection` (struct wrapping `uint`) | Prevents handle confusion |
| `HSteamListenSocket` | `ListenSocket` (struct wrapping `uint`) | Prevents handle confusion |
| `SteamAPICall_t` | `ulong` (internal only) | Only visible inside `CallResultAwaiter<T>` |
| `HSteamPipe` | `uint` (internal only) | Never exposed publicly |
| `uint32`, `uint64`, etc. | `uint`, `ulong` | Direct primitive mapping |

#### 8. Span / Buffer Handling for Fixed Arrays

Structs with fixed-size C arrays emit `fixed` buffer fields as `private unsafe`, with a generated safe `string` or `ReadOnlySpan<byte>` property accessor:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct FriendGameInfo_t
{
    private fixed byte m_szName[128];

    internal string Name
    {
        get
        {
            fixed (byte* p = m_szName)
                return Encoding.UTF8.GetString(p, 128).TrimEnd('\0');
        }
    }
}
```

Consumer code never uses `fixed` blocks directly.

#### 9. Unsupported Construct Policy

SDK constructs with no clean C# equivalent (union types, variadic args) are emitted as:

```csharp
[Obsolete("Manifold: Unsupported construct — <reason>. See ManifoldGen.skipped.json.")]
internal static partial void UnsupportedMethod(IntPtr self)
    => throw new NotSupportedException("Manifold: unsupported native construct.");
```

The generator logs a warning and records the skipped item in `ManifoldGen.skipped.json`. The generated output always compiles; gaps are visible and tracked.

#### 10. Known Struct Oddities

A small explicit list of structs that fall outside the standard pragma block pattern and require manual attention during generation and SDK upgrades. The generator treats this list as authoritative overrides — listed structs bypass automated pragma derivation and use their hardcoded `Pack` value.

| Struct | Pack | Reason |
|---|---|---|
| `SteamNetworkingMessage_t` | 1 | Explicit `#pragma pack(push, 1)` in `steamnetworkingtypes.h`, independent of the callback block |
| `CSteamID` (internal union) | 1 | Internal union layout, explicitly packed |
| `InputAnalogActionData_t` | 1 | `isteaminput.h` explicit Pack=1 block |
| `InputDigitalActionData_t` | 1 | `isteaminput.h` explicit Pack=1 block |
| `InputMotionData_t` | 1 | `isteaminput.h` explicit Pack=1 block |

When Valve releases a new SDK, review this list first. Valve has historically changed struct layout outside of the standard callback pragma blocks without announcement. Any struct in this list that fails its native `sizeof` validation test during an SDK upgrade should be treated as a potential Valve layout change — not a generator bug — until proven otherwise.

#### 11. SDK Diff & Upgrade Strategy

1. `ManifoldGen --diff old.json new.json` → human-readable diff of added/removed/changed API surface
2. Review the diff for breaking changes in Manifold-exposed interfaces
3. `ManifoldGen --generate new_sdk.json` → overwrites all generated files
4. Run the interop validation test suite — failures indicate ABI breaks or packing changes
5. Update public wrappers in `Manifold.Core` as needed
6. Bump version: `MAJOR.MINOR.PATCH-sdk{VERSION}`

Generated files are **never committed with local modifications**. Fixes go into the generator policy or the generator itself.

#### 12. Generated-Code Validation

A dedicated build-time pass (`Manifold.Interop.Validation`) verifies:

- Every `bool` P/Invoke declaration has `MarshalAs(U1)` — **Roslyn analyzer, build-time**
- Every generated struct's `Marshal.SizeOf<T>()` matches native `sizeof(T)` — **runtime test, per platform**
- Every `[LibraryImport]` entry point resolves in the native DLL — **runtime test**
- Every `k_iCallback` constant value matches the SDK JSON — **build-time**
- Platform smoke test: DLL loads, `SteamAPI_InitFlat` returns — **CI per platform**

---

## 7. Core Systems

### 7.1 `CallbackDispatcher`

Drives all Steam event delivery using `SteamAPI_ManualDispatch_*`. Called once per frame by `SteamLifecycle.RunCallbacks()` on the main/game thread. Thread safety: all calls and handler invocations must occur on the same thread; this is asserted in debug builds.

```csharp
internal static class CallbackDispatcher
{
    private static readonly Dictionary<int, List<Action<IntPtr>>>   _handlers    = new();
    private static readonly Dictionary<ulong, Action<IntPtr, bool>> _callResults = new();

    // Set by SteamLifecycle.Initialize() — captures the game thread ID at init time.
    // This is the authoritative thread; all Tick() calls must originate from it.
    internal static int GameThreadId { get; private set; }

    internal static void Register(int callbackId, Action<IntPtr> handler)   { ... }
    internal static void Unregister(int callbackId, Action<IntPtr> handler) { ... }
    internal static void RegisterCallResult(ulong apiCall, Action<IntPtr, bool> handler) { ... }
    internal static void CancelCallResult(ulong apiCall) { ... }
    internal static void CancelAll(Exception reason) { ... } // called during Dispose

    [Conditional("DEBUG")]
    private static void DebugAssertMainThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != GameThreadId)
            throw new InvalidOperationException(
                $"Manifold: RunCallbacks() called from thread {Thread.CurrentThread.ManagedThreadId} " +
                $"but game thread is {GameThreadId}. All Steam callbacks must run on the game thread.");
    }

    internal static void Tick(HSteamPipe pipe)
    {
        DebugAssertMainThread();
        SteamNative.SteamAPI_ManualDispatch_RunFrame(pipe);

        while (SteamNative.SteamAPI_ManualDispatch_GetNextCallback(pipe, out var msg))
        {
            try
            {
                if (msg.m_iCallback == SteamAPICallCompleted_t.k_iCallback
                    && _callResults.TryGetValue(msg.m_pubParam_apiCall, out var cr))
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

Wraps a `SteamAPICall_t` handle into a `Task<T>` with full `CancellationToken` support and an internal timeout safety net.

```csharp
internal static class CallResultAwaiter
{
    internal static Task<T> Await<T>(
        ulong apiCall,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null) where T : unmanaged
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var effectiveTimeout = timeout ?? SteamLifecycle.Current?.Options.CallResultTimeout
                               ?? TimeSpan.FromSeconds(30);

        // Register completion handler
        CallbackDispatcher.RegisterCallResult(apiCall, (ptr, ioFailed) =>
        {
            if (ioFailed) tcs.TrySetException(new SteamIOFailedException(apiCall));
            else          tcs.TrySetResult(Marshal.PtrToStructure<T>(ptr));
        });

        // Cancellation: stop caring about the result, don't abort the Steam operation
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);
        cts.Token.Register(() =>
        {
            CallbackDispatcher.CancelCallResult(apiCall);
            if (cancellationToken.IsCancellationRequested)
                tcs.TrySetCanceled(cancellationToken);
            else
                tcs.TrySetException(new SteamCallResultTimeoutException(apiCall, effectiveTimeout));
            cts.Dispose();
        });

        return tcs.Task;
    }
}
```

**Documented cancellation semantics**: Cancelling a `Task<T>` from an async Steam wrapper stops Manifold from caring about the result. It does **not** abort the operation on Steam's backend — the Steam servers may still process the request. This is documented on every async method.

### 7.3 `SteamId`

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

    public int CompareTo(SteamId other) => Value.CompareTo(other.Value);

    public static bool operator <(SteamId a, SteamId b)  => a.Value < b.Value;
    public static bool operator >(SteamId a, SteamId b)  => a.Value > b.Value;
    public static bool operator <=(SteamId a, SteamId b) => a.Value <= b.Value;
    public static bool operator >=(SteamId a, SteamId b) => a.Value >= b.Value;

    public static implicit operator ulong(SteamId id)    => id.Value;
    public static explicit operator SteamId(ulong value) => new(value);

    public static SteamId Parse(string s)
        => new(ulong.Parse(s, CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, out SteamId result)
    {
        if (ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out ulong v))
        { result = new(v); return true; }
        result = Invalid; return false;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    private string DebugDisplay => IsValid ? $"SteamId({Value})" : "SteamId(Invalid)";

    // JSON converter serialises as string to avoid JS number precision loss on 64-bit values
}
```

Similar typed handle structs: `NetConnection` (wraps `uint`), `ListenSocket` (wraps `uint`).

### 7.4 Exception Hierarchy

```
SteamException (base)
├── SteamInitException              — SteamAPI_Init failure, with EResult code
├── SteamIOFailedException          — call result returned with ioFailed=true
│     Properties:
│       ulong ApiCall               — the SteamAPICall_t handle that failed
│       EResult? BestEffortResult   — EResult from callback struct if readable;
│                                     null or unreliable when ioFailed=true because
│                                     the struct data may be garbage on transport failure.
│                                     Always check ioFailed context before trusting this.
│       string OperationHint        — human-readable name of the awaited operation,
│                                     e.g. "CreateLobby", set by the calling wrapper
├── SteamCallResultTimeoutException — internal timeout exceeded
├── SteamShutdownException          — pending call cancelled due to Dispose()
└── SteamDisposedException          — method called after Dispose()
```

> **EResult on `ioFailed`**: When `ioFailed = true`, Steam signals a transport/network-level failure. The callback struct was not validly populated by the Steam backend, so any EResult field within it may contain garbage. `BestEffortResult` is captured on a best-effort basis and must not be trusted unconditionally. When `ioFailed = false` and a Steam API operation returns a non-OK EResult (e.g. `LobbyCreated_t.m_eResult != EResult.OK`), the public API wrapper handles this by returning a typed failure value — not by throwing `SteamIOFailedException`. That distinction is intentional: `SteamIOFailedException` means the transport failed; a bad EResult in a successfully delivered callback is a logical failure handled at the wrapper level.

---

## 8. Networking Layer

### 8.1 `SteamNetworkingCore` — Internal Only

`SteamNetworkingCore` is an **internal implementation detail** of `Manifold.Core.Networking`. It is not part of any public or supported API surface. `SteamMultiplayerPeer` and `SteamLobbySession` in `Manifold.Godot` depend on it internally.

Rationale: once a type is public, it must be supported forever. Exposing raw networking primitives prematurely creates an unbounded support surface. If a legitimate need for raw networking access emerges in a future version, it will be added as a deliberate, versioned public API — not by accident.

### 8.2 Transfer Mode → Steam Send Flag Mapping

| Godot `TransferModeEnum` | Steam Send Flags | Notes |
|---|---|---|
| `Reliable` | `k_nSteamNetworkingSend_Reliable` (8) | Guaranteed, ordered within the reliable stream |
| `UnreliableOrdered` | `k_nSteamNetworkingSend_Unreliable` (0) | **Semantic gap — see below** |
| `Unreliable` | `k_nSteamNetworkingSend_UnreliableNoDelay` (4\|1) | No nagle, drop if queued too long |

**`UnreliableOrdered` degradation (v1 behaviour)**:

Steam's `ISteamNetworkingSockets` has no native ordered-unreliable transport mode. In v1, Manifold maps `UnreliableOrdered` to plain unreliable transport. Packets may arrive out of order or be dropped.

In debug builds and in the Godot editor, Manifold logs a **one-time per-peer warning** when `TransferMode` is set to `UnreliableOrdered`:

```
[Manifold] WARNING: TransferMode.UnreliableOrdered is set on SteamMultiplayerPeer,
but Steam has no native ordered-unreliable transport. Manifold is using unordered
unreliable delivery. In v1, Manifold does not emulate ordered-unreliable delivery.
Applications requiring stale-packet suppression or monotonic snapshot delivery must
implement sequence handling above the transport layer.
```

The warning fires once per peer instance, not once per packet.

### 8.3 `NoNagle` vs `NoDelay` — Distinct Semantics

These are two distinct peer flags with meaningfully different behaviour, derived directly from the SDK:

**`NoNagle`** (`k_nSteamNetworkingSend_NoNagle = 1`):
Disables Nagle's algorithm. Messages are sent immediately rather than being buffered to coalesce with subsequent messages. Use when a message is the last in a logical batch (e.g. final packet of a server simulation tick). Messages are **never dropped** by this flag — they are only sent sooner.

**`NoDelay`** (`k_nSteamNetworkingSend_NoDelay = 4`):
A stronger guarantee — if the message cannot be placed on the wire within approximately 200ms (connection still establishing, route negotiating, or send queue is backed up), the message is **silently dropped** with `k_EResultIgnored`. Also implies `NoNagle`. Use for data where a stale delivery is worse than no delivery — voice audio, real-time positional state.

They compose:

| `NoNagle` | `NoDelay` | Behaviour |
|---|---|---|
| false | false | Default — Nagle applies, never drops |
| true | false | Send promptly, never drop |
| false | true | N/A — NoDelay implies NoNagle |
| true | true | Send promptly, drop if delayed |

In Manifold: `NoNagle` is a per-peer flag (applied to all sends). `NoDelay` is also a per-peer flag. Both are documented on `SteamMultiplayerPeer` with these exact semantics.

### 8.4 Packet Size Guarantees

| Category | Limit | Source |
|---|---|---|
| Reliable message | ~1,048,576 bytes (1 MB) | `ISteamNetworkingSockets` documented max |
| Unreliable message | ~1,200 bytes practical MTU | Fragmented messages not guaranteed delivered |
| Internal header overhead | 2 bytes | Manifold-specific, transparent to Godot |

`_GetMaxPacketSize()` returns `1_048_576`.

### 8.4.1 `_GetPacket` Memory Strategy — Avoiding the GC Trap

Godot's `MultiplayerPeerExtension._GetPacket` requires an output `byte[]`. A naïve implementation doing `rBuffer = new byte[msgSize]` allocates a fresh managed array for every received packet. In a 60-tick multiplayer game with several peers this creates continuous GC pressure that will eventually produce frame spikes.

**Steam message ownership**: `SteamNetworkingMessage_t` owns its own data buffer (`m_pData`, `m_cbSize`). After copying the data you must call `Release()` on the message object — not free `m_pData` directly.

**Manifold's strategy — `ArrayPool<byte>` with Godot-safe handoff**:

`_Poll()` drains incoming Steam messages (see §8.4.2) into an internal `Queue<PooledPacket>`. For each message:
1. Rent a buffer from `ArrayPool<byte>.Shared` sized to `m_cbSize`
2. Copy `m_pData` → rented buffer (strip the 2-byte Manifold header, record channel + mode)
3. Call `msg.Release()` to free the Steam-owned buffer
4. Enqueue `PooledPacket(rentedBuffer, actualSize, senderId, channel, mode)`

`_GetPacket()` dequeues the next `PooledPacket`, sets `rBuffer = packet.Buffer` and `rBufferSize = packet.ActualSize`, then **returns the rented buffer to the pool immediately after** — it does not retain a reference.

This is safe because Godot's `MultiplayerPeerExtension` contract guarantees the engine copies `rBuffer[0..rBufferSize]` into its own internal buffer before `_GetPacket` returns control. The `byte[]` reference is not held across frames.

```csharp
private readonly Queue<PooledPacket> _incoming = new();

private record struct PooledPacket(
    byte[] Buffer,
    int    Size,
    int    Sender,
    int    Channel,
    MultiplayerPeer.TransferModeEnum Mode
);

public override Error _GetPacket(out byte[] rBuffer, out int rBufferSize)
{
    var pkt = _incoming.Dequeue();
    rBuffer     = pkt.Buffer;
    rBufferSize = pkt.Size;
    _currentPacket = pkt;   // held only until Return() call below
    ArrayPool<byte>.Shared.Return(pkt.Buffer);  // safe: Godot copies before returning
    return Error.Ok;
}
```

> **Phase 2 proof task — treat as blocking**: The safety of the pool-return strategy depends entirely on Godot copying `rBuffer[0..rBufferSize]` before `_GetPacket` returns control. This must be verified against Godot engine source (not assumed) as the **first task** when implementing `SteamMultiplayerPeer`. Read `MultiplayerPeerExtension` internals in the Godot source before writing any networking code. If the assumption does not hold, the pool-return strategy is unsafe and must be replaced with the fallback: a persistent per-peer `byte[]` sized at `_GetMaxPacketSize()`, allocated once at connect-time and reused every frame with no per-packet allocation.

### 8.4.2 PollGroup vs Per-Connection Polling

`ISteamNetworkingSockets` offers two receive paths:

- `ReceiveMessagesOnConnection(hConn, ...)` — drains one specific connection. CPU cost scales linearly with peer count — unacceptable for a host with many clients.
- `ReceiveMessagesOnPollGroup(hPollGroup, ...)` — drains **all connections in the group** in a single native call. `SteamNetworkingMessage_t::m_conn` identifies which connection each message came from.

**Manifold's policy**:

- **Host**: Creates one `HSteamNetPollGroup` at `CreateHost()`. Every accepted incoming connection is immediately added to the group via `SetConnectionPollGroup(hConn, _pollGroup)`. `_Poll()` calls `ReceiveMessagesOnPollGroup` once per frame to drain all peers in O(1) native calls regardless of peer count.
- **Client**: Has exactly one connection (to the server). Uses `ReceiveMessagesOnConnection` directly. No poll group created or needed.
- `DestroyPollGroup` is called during `_Close()` after all connections are closed.

```csharp
// Host _Poll() — one native call drains all peers
IntPtr[] msgPtrs = ArrayPool<IntPtr>.Shared.Rent(MaxMessagesPerFrame);
int count = SteamNative.ReceiveMessagesOnPollGroup(_pollGroup, msgPtrs, MaxMessagesPerFrame);
for (int i = 0; i < count; i++)
    ProcessMessage(msgPtrs[i]);   // copies data, calls Release()
ArrayPool<IntPtr>.Shared.Return(msgPtrs);
```

`MaxMessagesPerFrame` is configurable (default: 512). If a frame returns exactly `MaxMessagesPerFrame` messages, the next `_Poll()` will drain the remainder — messages are never lost, only deferred one frame.

### 8.5 Internal Packet Header Format

A 2-byte header is prepended to every packet by Manifold, transparent to the caller:

```
Byte 0 — Version + Kind
  Upper nibble [7:4] — Protocol version (currently always 0x0)
  Lower nibble [3:0] — Packet kind:
    0x0  Data           (normal RPC/game traffic)
    0x1  Handshake      (server→client: peer ID assignment)
    0x2  HandshakeAck   (client→server: confirmation)
    0x3  Disconnect     (graceful close with reason code)
    0x4–0xE Reserved
    0xF  Reserved (future use)

Byte 1 — Channel Index (0–255)
  Emulates ENet-style channels over a single Steam connection.
  Stripped before data is passed to Godot's MultiplayerAPI.
```

**Version field convention**: The upper nibble is reserved for future protocol format changes. Currently always `0x0`. When/if a format change is required, a new protocol version increments this nibble, and peers can negotiate or reject based on it. Defining this now costs nothing and prevents a future format break from being a compatibility nightmare.

### 8.6 Connection State Machine

```
                  CreateHost()                      CreateClient()
                      │                                  │
                      ▼                                  ▼
               ┌────────────┐                    ┌─────────────┐
               │  Listening │                    │  Connecting │
               └─────┬──────┘                    └──────┬──────┘
                     │ (incoming connection)             │ (Steam: Connected)
                     ▼                                  ▼
            ┌─────────────────────────────────────────────────────┐
            │                  Authenticating                      │
            │  Handshake exchange. Peer ID assignment in progress. │
            │  Timeout: 5 seconds → Disconnected (reason: Timeout) │
            └─────────────────────────┬───────────────────────────┘
                                      │ (handshake complete)
                                      ▼
            ┌─────────────────────────────────────────────────────┐
            │                    Connected                         │
            │  Normal operation. RPCs and data flowing.           │
            └──────┬─────────────────────────────┬────────────────┘
           Close() │                             │ Remote close / Steam error
                   ▼                             ▼
          ┌──────────────────┐       ┌───────────────────────────┐
          │  Disconnecting   │       │       Disconnected         │
          │  (draining)      │       │  DisconnectInfo populated  │
          └──────┬───────────┘       │  PeerDisconnected emitted  │
                 │ Steam confirms    └───────────────────────────┘
                 ▼
        ┌───────────────────────────┐
        │        Disconnected       │
        │  DisconnectInfo populated │
        │  PeerDisconnected emitted │
        └───────────────────────────┘
```

Transitions into `Disconnected` from any state are legal on Steam error or `SteamLifecycle.Dispose()`.

**State → `_GetConnectionStatus()` mapping:**

| Internal State | `ConnectionStatus` |
|---|---|
| `Listening`, `Connecting`, `Authenticating` | `Connecting` |
| `Connected`, `Disconnecting` | `Connected` |
| `Disconnected` | `Disconnected` |

### 8.7 Peer ID Handshake Protocol

1. Client establishes Steam connection → state: `Authenticating`
2. Server assigns next int ID (≥2; 1 is always server), sends handshake packet:
   ```
   [0x01][channel=0][peer_id: int32 little-endian]   // 6 bytes total
   ```
3. Client receives handshake, stores `_uniqueId`, sends ACK:
   ```
   [0x02][channel=0]   // 2 bytes total
   ```
4. Server receives ACK → emits `PeerConnected(clientId)`. Both sides enter `Connected`.
5. **Timeout**: if handshake is not completed within 5 seconds, connection is closed with `DisconnectInfo { Code = TimeoutCode, Reason = "Handshake timeout" }`.

### 8.8 Disconnect Reason Surfacing

```csharp
public readonly record struct DisconnectInfo(
    int    Code,          // ESteamNetConnectionEnd value
    string Reason,        // Human-readable string from Steam
    bool   WasLocalClose  // true = we called Close(), false = remote or network
);

// On SteamMultiplayerPeer:
public DisconnectInfo? LastDisconnectInfo { get; private set; }

// Core event:
public event Action<int, DisconnectInfo> PeerDisconnectedWithReason;

// Godot signal:
[Signal] public delegate void PeerDisconnectedWithReasonEventHandler(
    int godotPeerId, int code, string reason, bool wasLocal);
```

The standard `PeerDisconnected(int id)` signal is still emitted for full Godot `MultiplayerAPI` compatibility. `PeerDisconnectedWithReason` is additive.

### 8.9 `PeerIdMapper`

```csharp
internal sealed class PeerIdMapper
{
    private readonly Dictionary<SteamId, int>  _steamToGodot = new();
    private readonly Dictionary<int, SteamId>  _godotToSteam = new();
    private readonly Dictionary<uint, int>     _connToGodot  = new(); // HSteamNetConnection → godot id
    private int _nextId = 2;

    internal int     Register(SteamId steamId, uint connection);
    internal void    Remove(int godotId);
    internal SteamId GetSteamId(int godotId);
    internal int     GetGodotId(SteamId steamId);
    internal int     GetGodotId(uint connection);
    internal bool    TryGetGodotId(uint connection, out int godotId);
    internal void    Clear(); // called during peer Close()
}
```

### 8.10 `SteamMultiplayerPeer` (Godot Layer)

```csharp
public partial class SteamMultiplayerPeer : MultiplayerPeerExtension
{
    // ── Setup ────────────────────────────────────────────────────────────────
    public Error CreateHost(int virtualPort = 0);
    public Error CreateClient(SteamId hostSteamId, int virtualPort = 0);
    public Task<Error> HostWithLobbyAsync(ELobbyType type, int maxMembers,
        CancellationToken cancellationToken = default);
    public Task<Error> JoinLobbyAsync(SteamId lobbyId,
        CancellationToken cancellationToken = default);

    // ── Config ───────────────────────────────────────────────────────────────
    public bool NoNagle  { get; set; } = false;
    public bool NoDelay  { get; set; } = false;
    public bool UseRelay { get; set; } = true;

    // ── Disconnect info ───────────────────────────────────────────────────────
    public DisconnectInfo? LastDisconnectInfo { get; private set; }
    [Signal] public delegate void PeerDisconnectedWithReasonEventHandler(
        int peerId, int code, string reason, bool wasLocal);

    // ── All MultiplayerPeerExtension overrides (21 as of Godot 4.3–4.6; verify at implementation time) ────
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

---

## 9. API Wrapper Layer

Each interface wrapper in `Manifold.Core` follows this contract:

- **Synchronous** for non-blocking calls (`GetPersonaName()`, `GetSteamId()`)
- **`Task<T>` + `CancellationToken`** for call results (suffixed `Async`)
- **C# `event Action<T>`** for recurring callbacks
- **Strongly typed returns** — no `Dictionary`, no `long` where an enum belongs
- **XML doc comments** on every public member, including documented cancellation semantics on every async method

Corresponding Godot-facing wrappers in `Manifold.Godot` add `[Signal]` declarations backed by core events.

### Example: `SteamMatchmaking` (Core)

```csharp
public sealed class SteamMatchmaking
{
    // Call results
    public Task<LobbyCreated>     CreateLobbyAsync(ELobbyType type, int maxMembers,
        CancellationToken ct = default);
    public Task<LobbyEnterResult> JoinLobbyAsync(SteamId lobbyId,
        CancellationToken ct = default);
    public Task<LobbyList>        RequestLobbyListAsync(CancellationToken ct = default);

    // Non-blocking
    public void    LeaveLobby(SteamId lobbyId);
    public int     GetNumLobbyMembers(SteamId lobbyId);
    public SteamId GetLobbyMemberByIndex(SteamId lobbyId, int index);
    public SteamId GetLobbyOwner(SteamId lobbyId);
    public string  GetLobbyData(SteamId lobbyId, string key);
    public bool    SetLobbyData(SteamId lobbyId, string key, string value);
    public bool    SetLobbyMemberLimit(SteamId lobbyId, int maxMembers);
    public bool    SetLobbyType(SteamId lobbyId, ELobbyType type);
    public bool    SetLobbyJoinable(SteamId lobbyId, bool joinable);
    public void    InviteUserToLobby(SteamId lobbyId, SteamId invitee);

    // Recurring callbacks
    public event Action<LobbyDataUpdate>        LobbyDataUpdated;
    public event Action<LobbyChatUpdate>        LobbyChatUpdated;
    public event Action<LobbyInvite>            LobbyInviteReceived;
    public event Action<GameLobbyJoinRequested> JoinRequested;
}
```

---

## 10. Testing Strategy

### Philosophy

Coverage is contract-oriented. The goal is confidence that the public API contract is correct, interop is ABI-safe, and the networking state machine is sound. Test structure reflects what is being verified, not which class is touched.

### Test Categories

#### 1. Contract Tests
Every public method, property, and event on every public type has at least one test verifying documented behaviour. Use `FakeSteamBackend`. No Steam required.

**Coverage**: 100% of public API surface, enforced by a reflection-based test that enumerates public members and cross-references the test suite.

#### 2. Interop Verification Tests
Run against the actual native DLL (Steam not required — offline path):
- `Marshal.SizeOf<T>()` matches native `sizeof(T)` per platform (loaded via native helper)
- Every `[LibraryImport]` entry point resolves in the DLL
- Every `bool` declaration has `MarshalAs(U1)` — **Roslyn analyzer at build time**
- Every `k_iCallback` constant matches the SDK JSON

#### 3. State Machine / Protocol Tests
Drive `SteamMultiplayerPeer` and `PeerIdMapper` through every formal state transition using `FakeSteamBackend`:
- All paths in the connection state machine (§8.6)
- Handshake timeout → `Disconnected` with correct reason
- Graceful close → `DisconnectInfo` populated
- Remote close → `DisconnectInfo` populated
- Negative target peer routing (all-except)
- 2-byte header encode/decode round-trip
- All 21 `MultiplayerPeerExtension` override contracts
- `UnreliableOrdered` one-time warning fires exactly once per peer instance in debug builds

#### 4. Callback & Async Tests
- `CallbackDispatcher` routes to correct handler by `k_iCallback`
- `CallResultAwaiter<T>` resolves on success
- `CallResultAwaiter<T>` throws `SteamIOFailedException` on `ioFailed = true`
- `CallResultAwaiter<T>` throws `SteamCallResultTimeoutException` after timeout
- `CallResultAwaiter<T>` respects `CancellationToken` — `TaskCompletionSource` is not leaked
- `CancelAll()` during `Dispose()` faults all pending awaiters with `SteamShutdownException`
- `Dispose()` is idempotent — second call is silent no-op
- Thread assertion fires if `RunCallbacks()` is called off the registered main thread

#### 5. Godot Integration Tests (GdUnit4, headless)
- `SteamMultiplayerPeer` instantiates in a headless Godot process
- `Multiplayer.MultiplayerPeer = peer` accepted without error
- `@rpc` call round-trips between two peers in a loopback test
- `SteamManager` autoload initialises and exposes sub-interfaces correctly
- `[Signal]` fires when corresponding core `event` fires

#### 6. Platform Smoke Tests (CI — Windows, Linux, macOS)
- Native DLL loads without error
- Basic P/Invoke call completes without segfault or `DllNotFoundException`
- NuGet package builds and produces correct `runtimes/` structure

### Mock Backend Design

Rather than one interface method per P/Invoke call (which becomes unmaintainable), `ISteamNativeBackend` is split by capability:

```csharp
// Capability interfaces — implement only what a given test needs
public interface IMatchmakingBackend
{
    ulong CreateLobby(IntPtr self, ELobbyType type, int max);
    void  LeaveLobby(IntPtr self, ulong lobbyId);
    // ...
}

public interface INetworkingBackend
{
    uint  CreateListenSocketP2P(IntPtr self, int port, int numOpts, IntPtr opts);
    uint  ConnectP2P(IntPtr self, ref SteamNetworkingIdentity identity, int port, int numOpts, IntPtr opts);
    // ...
}

public interface IUserBackend
{
    ulong GetSteamID(IntPtr self);
    bool  BLoggedOn(IntPtr self);
    // ...
}

// Fake base — safe defaults for everything, override per test
public class FakeSteamBackend : IMatchmakingBackend, INetworkingBackend, IUserBackend
{
    public bool IsLoggedOn { get; set; } = true;
    public List<string> CallLog { get; } = new();
    // Configurable returns per method
}
```

The production `LiveSteamBackend` implements all capability interfaces by forwarding to `SteamNative` P/Invoke. Injected via constructor into all core wrappers.

---

## 11. Distribution & Packaging

### NuGet Package: `Manifold.Godot`

```
Manifold.Godot.nupkg
├── lib/net8.0/
│   ├── Manifold.Core.dll
│   └── Manifold.Godot.dll
├── runtimes/
│   ├── win-x64/native/steam_api64.dll
│   ├── linux-x64/native/libsteam_api.so
│   └── osx/native/libsteam_api.dylib
├── build/
│   └── Manifold.Godot.targets       ← MSBuild: auto-copy natives to output dir
└── README.md
```

`Manifold.Core` is also published as a separate, lighter package (no native binaries) for headless/server use cases.

### MSBuild Targets

The `.targets` file copies the correct platform native binary to the build output directory automatically. Zero manual setup for the developer.

### Versioning

`MAJOR.MINOR.PATCH-sdk{SDK_VERSION}`
Example: `1.0.0-sdk164`

SDK major bumps trigger a `MINOR` version increment. Breaking API changes trigger `MAJOR`.

---

## 12. Project Structure

```
Project Steamworks/
├── MASTER_DESIGN.md
├── MASTER_DESIGN_v0.1_backup.md
├── MASTER_DESIGN_v0.2_backup.md
├── steamworks_sdk_164/
└── src/
    ├── Manifold.Core/
    │   ├── Manifold.Core.csproj
    │   ├── Core/
    │   │   ├── SteamLifecycle.cs
    │   │   ├── SteamInitOptions.cs
    │   │   ├── CallbackDispatcher.cs
    │   │   ├── CallResultAwaiter.cs
    │   │   ├── SteamId.cs
    │   │   ├── NetConnection.cs
    │   │   ├── ListenSocket.cs
    │   │   ├── DisconnectInfo.cs
    │   │   └── SteamException.cs          ← full exception hierarchy
    │   ├── Networking/                    ← internal; SteamNetworkingCore lives here
    │   │   ├── SteamNetworkingCore.cs     ← internal sealed
    │   │   ├── PeerIdMapper.cs            ← internal sealed
    │   │   ├── HandshakeProtocol.cs       ← internal sealed
    │   │   └── PacketHeader.cs            ← internal sealed
    │   ├── Matchmaking/
    │   │   ├── SteamMatchmaking.cs
    │   │   └── Models/
    │   ├── Friends/
    │   │   ├── SteamFriends.cs
    │   │   └── Models/
    │   ├── User/
    │   │   ├── SteamUser.cs
    │   │   └── Models/
    │   ├── Utils/
    │   │   └── SteamUtils.cs
    │   ├── Testing/
    │   │   ├── FakeSteamBackend.cs
    │   │   ├── IMatchmakingBackend.cs
    │   │   ├── INetworkingBackend.cs
    │   │   └── IUserBackend.cs
    │   └── Interop/Generated/             ← DO NOT EDIT
    │       ├── SteamNative.Methods.cs
    │       ├── SteamNative.Structs.cs
    │       ├── SteamNative.Enums.cs
    │       └── SteamNative.Callbacks.cs
    ├── Manifold.Godot/
    │   ├── Manifold.Godot.csproj
    │   ├── SteamManager.cs
    │   ├── SteamSignalBridge.cs
    │   ├── Networking/
    │   │   ├── SteamMultiplayerPeer.cs
    │   │   └── SteamLobbySession.cs
    │   └── Nodes/
    │       ├── SteamMatchmakingNode.cs
    │       ├── SteamFriendsNode.cs
    │       └── SteamUserNode.cs
    ├── Manifold.Core.Tests/
    │   ├── Contract/
    │   ├── Interop/
    │   ├── StateMachine/
    │   └── Callbacks/
    └── Manifold.Godot.Tests/
        └── (GdUnit4 C# headless tests)

tools/
├── ManifoldGen/
│   ├── ManifoldGen.csproj
│   ├── Program.cs
│   ├── JsonModels.cs
│   ├── TypeMapper.cs
│   ├── PolicyValidator.cs         ← bool, struct size, keyword checks
│   ├── PackPragmaParser.cs        ← parses header #pragma pack directives
│   └── Emitters/
│       ├── MethodEmitter.cs
│       ├── StructEmitter.cs
│       ├── EnumEmitter.cs
│       └── CallbackEmitter.cs

samples/
└── ManifoldSample/                ← Godot 4 sample project

docs/
├── getting-started.md
├── lifecycle.md
├── multiplayer-peer.md
├── transfer-modes.md              ← includes UnreliableOrdered gap docs
├── nonagle-nodelay.md
├── callbacks-and-async.md
├── cancellation.md
└── api/
```

---

## 13. Development Phases

### Phase 1 — Foundation
- [ ] Solution and project structure
- [ ] **Native size validation helper** — thin C library exposing `sizeof(T)` for all callback structs; CMake build; CI pipeline on Windows x64, Linux x64, macOS. Must be complete before interop tests are meaningful.
- [ ] `ManifoldGen` — reads `steam_api.json` + header pack pragmas, emits full `Manifold.Core.Interop`
- [ ] `ManifoldGen` validator: bool marshalling, struct pack derivation, reserved keyword check
- [ ] `SteamId`, `NetConnection`, `ListenSocket`, `DisconnectInfo` types
- [ ] Full exception hierarchy (`SteamException` + all subtypes)
- [ ] `SteamLifecycle` with full shutdown contract
- [ ] `CallbackDispatcher` with `CancelAll()` for shutdown
- [ ] `CallResultAwaiter<T>` with `CancellationToken` and timeout
- [ ] `FakeSteamBackend` and capability interfaces
- [ ] Contract + interop + callback tests for all Phase 1 components
- [ ] Verified P/Invoke round-trip: init → get local SteamID → shutdown on all 3 platforms

### Phase 2 — Networking Core
- [ ] `PacketHeader` (2-byte format, version nibble, encode/decode)
- [ ] `HandshakeProtocol` (state machine, 5-second timeout)
- [ ] `PeerIdMapper`
- [ ] `SteamNetworkingCore` (internal — `ISteamNetworkingSockets` wrapper)
- [ ] `SteamMultiplayerPeer` (all 21 overrides, state machine, disconnect info, `UnreliableOrdered` warning)
- [ ] `SteamLobbySession`
- [ ] State machine, protocol, and Godot integration tests
- [ ] End-to-end: two peers connect via loopback, exchange data, disconnect cleanly

### Phase 3 — API Surface
- [ ] `SteamUser` + `SteamUserNode`
- [ ] `SteamFriends` + `SteamFriendsNode`
- [ ] `SteamMatchmaking` + `SteamMatchmakingNode`
- [ ] `SteamUtils`
- [ ] `SteamManager` autoload
- [ ] `SteamSignalBridge`
- [ ] Contract tests for all new public surface

### Phase 4 — Polish & Distribution
- [ ] NuGet packaging (`Manifold.Core` + `Manifold.Godot`) with native bundling and correct `runtimes/` structure
- [ ] MSBuild `.targets` for automatic native copy
- [ ] **Editor plugin** — `ManifoldEditorPlugin.cs` validating `steam/app_id` and `steam_appid.txt`; bundled in `Manifold.Godot` as editor-only addon + standalone Asset Library `.zip`
- [ ] Sample Godot project (lobby browser + 2-player RPC demo)
- [ ] Full documentation (lifecycle, transfer modes, NoNagle/NoDelay, cancellation, UnreliableOrdered gap)
- [ ] GitHub Actions CI: build + test on Windows, Linux, macOS (native helper build step first)
- [ ] Platform smoke tests in CI matrix

---

## 14. Key Decisions & Rationale

| Decision | Choice | Rationale |
|---|---|---|
| Package split | `Manifold.Core` + `Manifold.Godot` | Engine-agnostic core enables cheap unit testing, server builds, future use |
| Lifecycle model | `SteamLifecycle` (core) + `SteamManager` (ergonomic default) | Developers needing control bypass the autoload; most never think about it |
| Import method | `[LibraryImport]` | .NET 8 recommended; source-gen marshalling; better AOT than `[DllImport]` |
| Bool marshalling | `[MarshalAs(UnmanagedType.U1)]` always | Steam uses 1-byte C++ bool; .NET default is 4-byte BOOL; mismatch = silent stack corruption |
| Struct packing | Per-struct, header-derived | SDK uses platform-conditional Pack=4/8 for callbacks plus isolated Pack=1 structs; blanket Pack=1 is wrong |
| Struct size validation | Runtime test vs native sizeof | `steam_api.json` has no struct_size fields; native DLL is the only authoritative source |
| Callback dispatch | `SteamAPI_ManualDispatch_*` | Deterministic, frame-aligned, main-thread-safe |
| Call results | `Task<T>` + `CancellationToken` + internal timeout | Modern C#; cancellation stops caring about result (not aborting Steam op); timeout prevents TCS leaks |
| Shutdown contract | Ordered 6-step sequence, idempotent | Prevents use-after-free, dangling TCS objects, Godot signal emissions after native shutdown |
| `SteamNetworkingCore` | `internal sealed` | Public = supported forever; raw networking primitives are not ready for that commitment |
| Events vs signals | Core: `event`; Godot layer: `[Signal]` backed by `event` | Clean separation; typed C# events for C# code, signals for Godot/GDScript patterns |
| `UnreliableOrdered` | Maps to Unreliable + one-time debug warning | No native equivalent; honest mapping + runtime visibility beats silent degradation |
| `NoNagle` vs `NoDelay` | Distinct per-peer flags with documented semantics | Meaningfully different; conflating them produces wrong send behaviour |
| Packet header | 2 bytes: version nibble + kind nibble + channel byte | Internal control packets, channel emulation, future version negotiation — 2 bytes overhead is negligible |
| Mock backend | Capability interfaces + `FakeSteamBackend` base | One interface per P/Invoke call is unmaintainable; scoped capabilities let tests override only what they need |
| Strictly C# | No `[GlobalClass]`, no GDScript exposure | Manifold does not step on GodotSteam's territory |

---

## 15. Open Questions

- [ ] **ISteamGameServer (Phase 5)?** — Confirmed deferred. Not in scope for v1.
- [x] **Editor plugin — included in V1 (Phase 4)**. Protects developers from silent failures caused by misconfigured Project Settings strings (`steam/app_id`, etc.) or a missing `steam_appid.txt`. A minimal `EditorPlugin` (~150 lines of C#) that:
  - Validates `steam/app_id` is set and non-zero on project open and build
  - Checks that `steam_appid.txt` exists in the Godot project root
  - Shows an actionable editor warning panel (not just a console print) if either check fails
  - Optionally offers a "Fix it" button that writes `steam_appid.txt` from the configured app ID
  Distributed as an editor-only Godot addon bundled inside `Manifold.Godot` (activated only in editor builds), plus a standalone `.zip` for the Godot Asset Library. Does not affect runtime behaviour.
- [ ] **`UnreliableOrdered` application-layer sequencing in v2?** — Accept documented degradation for v1. Revisit based on community feedback after launch.
- [ ] **Facepunch.Steamworks coexistence?** — Both libraries call `SteamAPI_Init`. Using both is unsupported and must be documented. No compatibility shim planned.

---

*This document is the canonical reference for Manifold's design. All implementation decisions must be reconciled against it. Update this document before implementing any significant architectural change.*
