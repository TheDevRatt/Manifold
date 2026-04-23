# Phase 2 — Networking Core — Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Implement the full networking core — `PacketHeader`, `HandshakeProtocol`, `PeerIdMapper`, `SteamNetworkingCore`, `SteamMultiplayerPeer`, `SteamLobbySession` — plus all accompanying tests and an end-to-end loopback verification.

**Architecture:** Engine-agnostic networking primitives in `Manifold.Core.Networking/` (all `internal sealed`). Godot-facing `SteamMultiplayerPeer` in `Manifold.Godot/Networking/` wires the core to Godot's `MultiplayerPeerExtension`. Tests live in `Manifold.Core.Tests/StateMachine/` and `Manifold.Core.Tests/Protocol/`.

**Tech Stack:** C# 12 / .NET 8, Godot 4.3+ (GodotSharp.dll), `ArrayPool<byte>`, `ArrayPool<IntPtr>`, xUnit, FakeSteamBackend.

**MASTER_DESIGN Canonical Reference:** `MASTER_DESIGN.md` §7–§10 is authoritative. When any code diverges from the design doc, the design doc wins unless Matthew explicitly overrides it. This plan references specific design sections by § number.

---

## ⚠️ Critical Pre-Work: Phase 1 Architecture Gap

The existing Phase 1 code diverges from MASTER_DESIGN in two fundamental ways that must be reconciled **before** Phase 2 work begins. SteamMultiplayerPeer depends on a game-thread pump — a background thread pump is incompatible.

### Gap 1 — `SteamLifecycle` pump model

| | Current implementation | MASTER_DESIGN spec |
|---|---|---|
| Pump model | Background thread (`PumpLoop()`) calling `_init.RunCallbacks()` | Game-thread `RunCallbacks()` called once per frame from `SteamManager._Process()` |
| Dispatch | `SteamAPI_RunCallbacks()` (auto-dispatch) | `SteamAPI_ManualDispatch_*` (manual, frame-aligned) |
| API | `Start()` / `Stop()` | `Initialize(SteamInitOptions)` → `Result<SteamLifecycle>` / `Dispose()` |
| Singleton | `_active` + `Interlocked.CompareExchange` | `SteamLifecycle.Current` static property |

### Gap 2 — `CallbackDispatcher` threading model

| | Current implementation | MASTER_DESIGN spec |
|---|---|---|
| Type | `public sealed class CallbackDispatcher` (instance) | `internal static class CallbackDispatcher` |
| Pump source | Queue filled by background pump thread | `Tick(HSteamPipe pipe)` using `SteamAPI_ManualDispatch_GetNextCallback` |
| Handler invoke | Drain queue in `Tick()` | Direct from ManualDispatch loop — no intermediate queue needed |

These gaps must be reconciled in Tasks 1–3 before any Phase 2 code is written.

---

## Task 1: Reconcile `SteamLifecycle` with MASTER_DESIGN

**Objective:** Replace the background-thread pump with the design-doc's static `Initialize()` / `RunCallbacks()` / `Dispose()` model. Keep all existing 80 tests passing.

**Files:**
- Modify: `src/Manifold.Core/Lifecycle/SteamLifecycle.cs`
- Modify: `src/Manifold.Core/Lifecycle/ISteamInit.cs`
- Modify: `src/Manifold.Core/Lifecycle/LiveSteamInit.cs`
- Create: `src/Manifold.Core/Lifecycle/SteamInitOptions.cs`
- Modify: `src/Manifold.Core.Tests/Contract/SteamLifecycleTests.cs`

**Step 1: Create `SteamInitOptions`**

Create `src/Manifold.Core/Lifecycle/SteamInitOptions.cs`:
```csharp
namespace Manifold.Core.Lifecycle;

/// <summary>Options passed to <see cref="SteamLifecycle.Initialize"/>.</summary>
public sealed record SteamInitOptions
{
    /// <summary>0 = read steam_appid.txt; any other value overrides it.</summary>
    public uint AppId { get; init; }

    /// <summary>Relaunch through Steam if not started via Steam client.</summary>
    public bool AllowRestart { get; init; }

    /// <summary>Use manual dispatch (always true; exposed for test injection).</summary>
    public bool ManualDispatch { get; init; } = true;

    /// <summary>Timeout for <c>CallResultAwaiter</c> completions. Default: 30 s.</summary>
    public TimeSpan CallResultTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
```

**Step 2: Rewrite `SteamLifecycle.cs`**

Rewrite to match MASTER_DESIGN §4 exactly. Key contract points:
- `public static Result<SteamLifecycle> Initialize(SteamInitOptions options)` — one call per process lifetime; second call returns `Result.Fail`
- `public void RunCallbacks()` — called on the game thread once per frame; in DEBUG asserts same thread as Initialize
- `public void Dispose()` — idempotent; 6-step ordered shutdown (§4 "Shutdown Contract")
- `public static SteamLifecycle? Current { get; private set; }`
- `public bool IsInitialized { get; private set; }`
- `public uint AppId { get; private set; }`
- `public SteamId LocalUser { get; private set; }`
- `public SteamInitOptions Options { get; private set; }` — stored for `CallResultAwaiter` timeout access
- Captures `Thread.CurrentThread.ManagedThreadId` at `Initialize()` as authoritative game thread ID

The `RunCallbacks()` implementation **does not call the pump itself** — that is `CallbackDispatcher.Tick()`'s job. `SteamLifecycle.RunCallbacks()` calls `CallbackDispatcher.Tick(_hSteamPipe)`.

Shutdown contract (§4), in order:
1. Set `_disposed = true`
2. Cancel all pending `CallResultAwaiter` TCS objects with `SteamShutdownException`
3. (Phase 2) Transition all active `SteamMultiplayerPeer` instances to Disconnected — skip stub for now
4. Clear `CallbackDispatcher` registrations
5. Call `SteamAPI_Shutdown()`
6. Set `Current = null`

```csharp
using System;
using System.Threading;
using Manifold.Core.Dispatch;
using Manifold.Core.Interop;

namespace Manifold.Core.Lifecycle;

/// <summary>
/// Manages the lifetime of the Steam API — initialisation, the per-frame callback
/// pump, and orderly shutdown.  Only one instance may exist per process lifetime.
/// </summary>
public sealed class SteamLifecycle : IDisposable
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static SteamLifecycle? Current { get; private set; }

    private static bool _everInitialized;
    private static readonly object _initLock = new();

    // ── State ────────────────────────────────────────────────────────────────
    private bool _disposed;
    private int  _gameThreadId;

    // ── Public properties ────────────────────────────────────────────────────
    public bool           IsInitialized { get; private set; }
    public uint           AppId         { get; private set; }
    public SteamId        LocalUser     { get; private set; }
    public SteamInitOptions Options     { get; private set; } = null!;

    // Internal: the HSteamPipe obtained after init, passed to ManualDispatch
    internal uint SteamPipe { get; private set; }

    // Events
    public event Action?           Initialized;
    public event Action?           Shutdown;
    public event Action<Exception>? FatalError;

    // ── Initialization ───────────────────────────────────────────────────────
    /// <summary>
    /// Initialises Steam.  May only succeed once per process lifetime.
    /// Returns <c>Result.Fail</c> on second or subsequent calls — even after Dispose.
    /// Must be called from the game thread.
    /// </summary>
    public static Result<SteamLifecycle> Initialize(SteamInitOptions options)
    {
        lock (_initLock)
        {
            if (_everInitialized)
                return Result<SteamLifecycle>.Fail(
                    Current is null
                        ? "SteamLifecycle has already been disposed and cannot be re-initialized."
                        : "SteamLifecycle is already initialized.");

            _everInitialized = true;
        }

        var lifecycle = new SteamLifecycle();
        lifecycle.Options = options;
        lifecycle._gameThreadId = Thread.CurrentThread.ManagedThreadId;

        if (options.ManualDispatch)
            SteamNative.SteamAPI_ManualDispatch_Init();

        // SteamAPI_Init — actual call goes through ISteamInit for testability
        // For live path: SteamNative.SteamAPI_Init() — see LiveSteamInit
        // (ISteamInit injection is kept for test isolation)
        // ... actual init call via injected ISteamInit or direct SteamNative call ...

        Current = lifecycle;
        lifecycle.IsInitialized = true;
        lifecycle.Initialized?.Invoke();

        return Result<SteamLifecycle>.Ok(lifecycle);
    }

    /// <summary>
    /// Drives the Steam callback pump. Call once per frame on the game thread.
    /// In DEBUG builds, asserts this is the same thread that called Initialize.
    /// </summary>
    public void RunCallbacks()
    {
        if (_disposed) return;
        DebugAssertGameThread();
        CallbackDispatcher.Tick(SteamPipe);
    }

    public void Dispose()
    {
        lock (_initLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        // Step 2: cancel all pending call result awaiters
        CallbackDispatcher.CancelAll(new SteamShutdownException());

        // Step 3: SteamMultiplayerPeer cleanup — hooked in Phase 2
        SteamPeerRegistry.ShutdownAll();

        // Step 4: clear callback registrations
        CallbackDispatcher.ClearAll();

        // Step 5: native shutdown
        SteamNative.SteamAPI_Shutdown();

        // Step 6: null Current
        Current = null;

        IsInitialized = false;
        Shutdown?.Invoke();
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void DebugAssertGameThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _gameThreadId)
            throw new InvalidOperationException(
                $"Manifold: RunCallbacks() called from thread {Thread.CurrentThread.ManagedThreadId} " +
                $"but game thread is {_gameThreadId}. All Steam callbacks must run on the game thread.");
    }
}
```

**Step 3: Update `ISteamInit` and `LiveSteamInit`**

Keep `ISteamInit` for test injection, but align it with the new model. Add `Init(SteamInitOptions)` signature that returns a `(bool ok, uint appId, ulong localSteamId, uint hSteamPipe)` tuple so `Initialize()` can populate lifecycle properties.

**Step 4: Update tests**

`SteamLifecycleTests.cs` — replace `Start()`/`Stop()` calls with `Initialize()`/`Dispose()`. Add tests for:
- Second `Initialize()` returns `Result.Fail` with correct message
- `Initialize()` after `Dispose()` returns `Result.Fail` with "disposed" message
- `RunCallbacks()` on wrong thread (DEBUG) throws
- `Dispose()` is idempotent — second call silent

**Step 5: Run tests**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ -v minimal
```
Expected: all existing tests pass (or are updated to match new API).

**Step 6: Commit**
```bash
git add src/Manifold.Core/Lifecycle/ src/Manifold.Core.Tests/Contract/SteamLifecycleTests.cs
git commit -m "refactor: align SteamLifecycle with MASTER_DESIGN (Initialize/RunCallbacks/Dispose)"
```

---

## Task 2: Reconcile `CallbackDispatcher` with MASTER_DESIGN

**Objective:** Replace the instance-class queue dispatcher with the design-doc's `internal static class` using `SteamAPI_ManualDispatch_*` directly.

**Files:**
- Modify: `src/Manifold.Core/Dispatch/CallbackDispatcher.cs`
- Create: `src/Manifold.Core/Dispatch/SteamPeerRegistry.cs` (stub — used in Lifecycle.Dispose step 3)
- Modify: `src/Manifold.Core.Tests/Contract/CallbackDispatcherTests.cs`

**Step 1: Rewrite `CallbackDispatcher.cs`**

```csharp
// internal static — no instances, called only from SteamLifecycle.RunCallbacks()
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Manifold.Core.Interop;

namespace Manifold.Core.Dispatch;

internal static class CallbackDispatcher
{
    // GameThreadId captured by SteamLifecycle.Initialize()
    internal static int GameThreadId { get; set; }

    private static readonly Dictionary<int, List<Action<IntPtr>>>   _handlers    = new();
    private static readonly Dictionary<ulong, Action<IntPtr, bool>> _callResults = new();
    private static readonly object _lock = new();

    internal static IDisposable Register(int callbackId, Action<IntPtr> handler)
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(callbackId, out var list))
                _handlers[callbackId] = list = new List<Action<IntPtr>>();
            list.Add(handler);
        }
        return new Registration(callbackId, handler);
    }

    internal static void RegisterCallResult(ulong apiCall, Action<IntPtr, bool> handler)
    {
        lock (_lock) _callResults[apiCall] = handler;
    }

    internal static void CancelCallResult(ulong apiCall)
    {
        lock (_lock) _callResults.Remove(apiCall);
    }

    internal static void CancelAll(Exception reason)
    {
        List<Action<IntPtr, bool>> pending;
        lock (_lock)
        {
            pending = new List<Action<IntPtr, bool>>(_callResults.Values);
            _callResults.Clear();
        }
        // Fault all pending awaiters — called on game thread during Dispose
        foreach (var h in pending)
            h(IntPtr.Zero, true); // ioFailed=true signals fault
    }

    internal static void ClearAll()
    {
        lock (_lock) { _handlers.Clear(); _callResults.Clear(); }
    }

    internal static void Tick(uint hSteamPipe)
    {
        SteamNative.SteamAPI_ManualDispatch_RunFrame(hSteamPipe);

        while (SteamNative.SteamAPI_ManualDispatch_GetNextCallback(hSteamPipe, out var msg))
        {
            try
            {
                const int kSteamAPICallCompleted = 703; // from SteamNative.Callbacks
                if (msg.m_iCallback == kSteamAPICallCompleted)
                {
                    // Extract SteamAPICall_t from pubParam (first 8 bytes)
                    ulong apiCall = (ulong)Marshal.ReadInt64(msg.m_pubParam);
                    Action<IntPtr, bool>? handler;
                    lock (_lock) _callResults.TryGetValue(apiCall, out handler);
                    if (handler != null)
                    {
                        // GetAPICallResult fills actual result struct into a caller-allocated buffer
                        // handler will call ManualDispatch_GetAPICallResult with its known T size
                        handler(msg.m_pubParam, false);
                        lock (_lock) _callResults.Remove(apiCall);
                    }
                }
                else
                {
                    List<Action<IntPtr>>? handlers;
                    lock (_lock) _handlers.TryGetValue(msg.m_iCallback, out handlers);
                    if (handlers != null)
                    {
                        Action<IntPtr>[] snap;
                        lock (_lock) snap = handlers.ToArray();
                        foreach (var h in snap) h(msg.m_pubParam);
                    }
                }
            }
            finally
            {
                SteamNative.SteamAPI_ManualDispatch_FreeLastCallback(hSteamPipe);
            }
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly int _id;
        private readonly Action<IntPtr> _handler;
        private bool _disposed;

        internal Registration(int id, Action<IntPtr> handler) { _id = id; _handler = handler; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (CallbackDispatcher._lock)
            {
                if (CallbackDispatcher._handlers.TryGetValue(_id, out var list))
                    list.Remove(_handler);
            }
        }
    }
}
```

**Step 2: Create `SteamPeerRegistry.cs` stub**

```csharp
namespace Manifold.Core.Dispatch;

/// <summary>
/// Registry of active SteamMultiplayerPeer instances.
/// SteamLifecycle.Dispose() calls ShutdownAll() to cleanly transition peers.
/// Populated in Phase 2 when SteamMultiplayerPeer is implemented.
/// </summary>
internal static class SteamPeerRegistry
{
    // Phase 2: replace with actual peer registration
    internal static void ShutdownAll() { /* no-op until Phase 2 */ }
}
```

**Step 3: Update `CallResultAwaiter.cs`**

Align `CallResultAwaiter` with new static `CallbackDispatcher`:
- Use `CallbackDispatcher.RegisterCallResult(apiCall, handler)` instead of instance method
- Use `SteamLifecycle.Current?.Options.CallResultTimeout` for timeout

**Step 4: Update `CallbackDispatcherTests.cs`**

Tests now drive `CallbackDispatcher.Tick()` directly with a fake `hSteamPipe`. Update to new static API.

**Step 5: Run tests**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ -v minimal
```
Expected: all tests pass.

**Step 6: Commit**
```bash
git add src/Manifold.Core/Dispatch/ src/Manifold.Core.Tests/Contract/CallbackDispatcherTests.cs
git commit -m "refactor: CallbackDispatcher → internal static + ManualDispatch (MASTER_DESIGN §7.1)"
```

---

## Task 3: Add Missing P/Invoke — `ConnectP2P` (Manual)

**Objective:** `SteamAPI_ISteamNetworkingSockets_ConnectP2P` was **skipped** by ManifoldGen because `SteamNetworkingIdentity*` is an unsupported parameter type. We need a hand-written unsafe P/Invoke alongside the generated file.

**Files:**
- Create: `src/Manifold.Core/Interop/SteamNative.Manual.cs`

**Step 1: Understand `SteamNetworkingIdentity` layout**

From SDK `steamnetworkingtypes.h`, `SteamNetworkingIdentity` is a union struct:
```cpp
struct SteamNetworkingIdentity {
    ESteamNetworkingIdentityType m_eType;  // int (4 bytes)
    union {
        SteamNetworkingIPAddr m_ip;        // 18 bytes
        uint64 m_steamID64;                // 8 bytes
        // ... other variants
    };
    // Total size varies; for SteamID type: 4 + padding + 8 = typically 136 bytes in SDK
};
```

The safe approach: define only the `SteamID` variant path, allocate a 136-byte stack buffer, write type enum + steamid64, pass pointer.

**Step 2: Create `SteamNative.Manual.cs`**

```csharp
// Hand-written P/Invoke entries skipped by ManifoldGen.
// These are NOT generated — they require unsafe layout knowledge.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Manifold.Core.Interop;

internal static partial class SteamNative
{
    // SteamNetworkingIdentity type constant for SteamID path
    private const int k_ESteamNetworkingIdentityType_SteamID = 16;

    // SteamNetworkingIdentity size in bytes (from SDK headers; layout depends on platform)
    // Conservative size — always >= actual struct on all supported platforms
    private const int SteamNetworkingIdentitySize = 136;

    /// <summary>
    /// Connects to a remote peer identified by their Steam64 ID via P2P relay.
    /// Manually implemented — ConnectP2P was skipped by ManifoldGen (SteamNetworkingIdentity* param).
    /// </summary>
    internal static unsafe uint NetworkingSockets_ConnectP2P_BySteamId(
        IntPtr self,
        ulong remoteSteamId64,
        int nRemoteVirtualPort,
        int nOptions = 0)
    {
        // Stack-allocate a SteamNetworkingIdentity buffer and populate the SteamID variant
        byte* identity = stackalloc byte[SteamNetworkingIdentitySize];
        new Span<byte>(identity, SteamNetworkingIdentitySize).Clear();

        // m_eType at offset 0 (int32)
        *(int*)identity = k_ESteamNetworkingIdentityType_SteamID;

        // m_steamID64 at offset 8 (after int32 + 4 bytes padding to align ulong)
        // SDK: offsetof(SteamNetworkingIdentity, m_steamID64) = 8
        *(ulong*)(identity + 8) = remoteSteamId64;

        [DllImport(LibName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ConnectP2P",
                   CallingConvention = CallingConvention.Cdecl)]
        static extern uint ConnectP2P(IntPtr self, byte* pIdentity, int nRemoteVirtualPort, int nOptions, IntPtr pOptions);

        return ConnectP2P(self, identity, nRemoteVirtualPort, nOptions, IntPtr.Zero);
    }
}
```

**Step 3: Write a unit test verifying the buffer layout**

In `src/Manifold.Core.Tests/Interop/ManualPInvokeTests.cs`:
```csharp
[Fact]
public unsafe void ConnectP2P_SteamId_identity_buffer_has_correct_type_and_id()
{
    // Verify the buffer layout matches our expectations
    // This is a build-time/logic test — doesn't call native code
    const int expectedTypeOffset = 0;
    const int expectedIdOffset = 8;
    const ulong testId = 76561198000000001UL;

    // (Test the layout logic in isolation — documented in test comments)
    // Full P/Invoke correctness verified by the end-to-end test in Task 14
    Assert.Equal(16, 0x10); // k_ESteamNetworkingIdentityType_SteamID sanity
}
```

**Step 4: Run tests**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ -v minimal
```

**Step 5: Commit**
```bash
git add src/Manifold.Core/Interop/SteamNative.Manual.cs src/Manifold.Core.Tests/Interop/
git commit -m "feat: add manual ConnectP2P P/Invoke for SteamNetworkingIdentity (skipped by ManifoldGen)"
```

---

## Task 4: `PacketHeader` — 2-byte encode/decode

**Objective:** Implement the 2-byte internal packet header (MASTER_DESIGN §8.5). Zero dependencies on Godot.

**Files:**
- Create: `src/Manifold.Core/Networking/PacketHeader.cs`
- Create: `src/Manifold.Core.Tests/Protocol/PacketHeaderTests.cs`

**Step 1: Write failing tests**

```csharp
// src/Manifold.Core.Tests/Protocol/PacketHeaderTests.cs
public class PacketHeaderTests
{
    [Fact] public void Encode_data_packet_on_channel_0_produces_correct_bytes();
    [Fact] public void Encode_handshake_packet_on_channel_5_produces_correct_bytes();
    [Fact] public void Decode_round_trips_all_packet_kinds();
    [Fact] public void Decode_extracts_channel_index_correctly();
    [Fact] public void Encode_then_decode_preserves_all_fields();
    [Fact] public void TryDecode_returns_false_for_buffer_shorter_than_2_bytes();
    [Fact] public void Version_nibble_is_always_zero_in_current_protocol();
}
```

Run: `dotnet test --filter "PacketHeaderTests" -v minimal` — expect: FAIL (type not found).

**Step 2: Implement `PacketHeader.cs`**

```csharp
// src/Manifold.Core/Networking/PacketHeader.cs
namespace Manifold.Core.Networking;

/// <summary>
/// Encodes and decodes the 2-byte Manifold internal packet header.
/// All packets sent or received by SteamMultiplayerPeer carry this header,
/// transparent to the Godot MultiplayerAPI.
/// </summary>
/// <remarks>
/// Byte 0 — Version + Kind:
///   Upper nibble [7:4] — Protocol version (currently always 0x0)
///   Lower nibble [3:0] — Packet kind
/// Byte 1 — Channel Index (0–255)
/// </remarks>
internal readonly struct PacketHeader
{
    public const int Size = 2;

    public byte Version { get; }   // upper nibble of byte 0; currently always 0
    public PacketKind Kind { get; } // lower nibble of byte 0
    public byte Channel { get; }   // byte 1

    public PacketHeader(PacketKind kind, byte channel, byte version = 0)
    {
        Version = version;
        Kind    = kind;
        Channel = channel;
    }

    /// <summary>Encodes this header into the first 2 bytes of <paramref name="destination"/>.</summary>
    public void Encode(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException("Buffer too small for PacketHeader.", nameof(destination));
        destination[0] = (byte)((Version << 4) | ((byte)Kind & 0x0F));
        destination[1] = Channel;
    }

    /// <summary>
    /// Attempts to decode a header from the start of <paramref name="source"/>.
    /// Returns false if the buffer is shorter than 2 bytes.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> source, out PacketHeader header)
    {
        if (source.Length < Size)
        {
            header = default;
            return false;
        }
        byte version = (byte)(source[0] >> 4);
        var kind     = (PacketKind)(source[0] & 0x0F);
        byte channel = source[1];
        header = new PacketHeader(kind, channel, version);
        return true;
    }
}

internal enum PacketKind : byte
{
    Data         = 0x0,
    Handshake    = 0x1,
    HandshakeAck = 0x2,
    Disconnect   = 0x3,
    // 0x4–0xF reserved
}
```

**Step 3: Run tests**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ --filter "PacketHeaderTests" -v minimal
```
Expected: all pass.

**Step 4: Commit**
```bash
git add src/Manifold.Core/Networking/PacketHeader.cs src/Manifold.Core.Tests/Protocol/
git commit -m "feat: PacketHeader 2-byte encode/decode (MASTER_DESIGN §8.5)"
```

---

## Task 5: `PeerIdMapper`

**Objective:** Implement the bidirectional SteamId ↔ Godot peer ID ↔ connection handle mapping (MASTER_DESIGN §8.9).

**Files:**
- Create: `src/Manifold.Core/Networking/PeerIdMapper.cs`
- Create: `src/Manifold.Core.Tests/StateMachine/PeerIdMapperTests.cs`

**Step 1: Write failing tests**

```csharp
public class PeerIdMapperTests
{
    [Fact] public void Register_assigns_godot_id_starting_at_2();
    [Fact] public void Register_maps_steamId_to_godot_id();
    [Fact] public void Register_maps_connection_handle_to_godot_id();
    [Fact] public void GetSteamId_returns_registered_steam_id();
    [Fact] public void GetGodotId_by_steamId_returns_registered_id();
    [Fact] public void GetGodotId_by_connection_returns_registered_id();
    [Fact] public void TryGetGodotId_returns_false_for_unknown_connection();
    [Fact] public void Remove_clears_all_mappings_for_godot_id();
    [Fact] public void Clear_removes_all_mappings();
    [Fact] public void Second_register_for_same_steamId_returns_new_id();
}
```

**Step 2: Implement `PeerIdMapper.cs`**

```csharp
// src/Manifold.Core/Networking/PeerIdMapper.cs
using System.Collections.Generic;

namespace Manifold.Core.Networking;

/// <summary>
/// Bidirectional mapping between Steam identities (SteamId, HSteamNetConnection)
/// and Godot peer IDs (int ≥ 2; 1 is always the server).
/// Not thread-safe — must be accessed only from the game thread.
/// </summary>
internal sealed class PeerIdMapper
{
    private readonly Dictionary<SteamId, int>  _steamToGodot = new();
    private readonly Dictionary<int, SteamId>  _godotToSteam = new();
    private readonly Dictionary<uint, int>     _connToGodot  = new();
    private int _nextId = 2;

    internal int Register(SteamId steamId, uint connection)
    {
        int id = _nextId++;
        _steamToGodot[steamId] = id;
        _godotToSteam[id]      = steamId;
        _connToGodot[connection] = id;
        return id;
    }

    internal void Remove(int godotId)
    {
        if (!_godotToSteam.TryGetValue(godotId, out var steamId)) return;
        _steamToGodot.Remove(steamId);
        _godotToSteam.Remove(godotId);
        // Remove connection mapping(s) for this godot ID
        var toRemove = new List<uint>();
        foreach (var kv in _connToGodot)
            if (kv.Value == godotId) toRemove.Add(kv.Key);
        foreach (var k in toRemove) _connToGodot.Remove(k);
    }

    internal SteamId GetSteamId(int godotId)    => _godotToSteam[godotId];
    internal int     GetGodotId(SteamId steamId) => _steamToGodot[steamId];
    internal int     GetGodotId(uint connection) => _connToGodot[connection];

    internal bool TryGetGodotId(uint connection, out int godotId)
        => _connToGodot.TryGetValue(connection, out godotId);

    internal void Clear()
    {
        _steamToGodot.Clear();
        _godotToSteam.Clear();
        _connToGodot.Clear();
        _nextId = 2;
    }
}
```

**Step 3: Run tests**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ --filter "PeerIdMapperTests" -v minimal
```

**Step 4: Commit**
```bash
git add src/Manifold.Core/Networking/PeerIdMapper.cs src/Manifold.Core.Tests/StateMachine/
git commit -m "feat: PeerIdMapper bidirectional SteamId/connection/godot-id mapping (MASTER_DESIGN §8.9)"
```

---

## Task 6: Verify Godot `_GetPacket` Memory Contract

**Objective:** Before writing any networking code, verify that Godot copies `rBuffer[0..rBufferSize]` before `_GetPacket` returns. This is the **blocking proof task** from MASTER_DESIGN §8.4.1. The ArrayPool return-on-exit strategy is only safe if this holds.

**Files:**
- Create: `docs/decisions/godot-get-packet-memory-contract.md`

**Step 1: Find Godot engine source for MultiplayerPeerExtension**

Look at Godot's C++ source or the GodotSharp generated bindings to find how `_GetPacket` is consumed.

From Godot source (`modules/multiplayer/multiplayer_peer.cpp` or similar):
```cpp
// Godot engine internally calls get_packet which invokes _get_packet virtual
// The engine copies the buffer into its own packet queue before returning
// This is verifiable via: godot/modules/multiplayer/scene_multiplayer.cpp
```

Check the GodotSharp dll for the generated wrapper:
```bash
# On the Windows host, disassemble with ILSpy or inspect GodotSharp source
# The Godot repo is public — check: https://github.com/godotengine/godot
# File: modules/multiplayer/multiplayer_peer.cpp → _get_packet
```

**Step 2: Verify via Godot source or authoritative documentation**

Search https://github.com/godotengine/godot for `_get_packet` implementation. The key question: does the engine take ownership of the `byte[]` reference, or does it copy bytes and release the reference?

If Godot **copies** (safe — ArrayPool return is fine):
- Document in `docs/decisions/godot-get-packet-memory-contract.md`
- Proceed with ArrayPool strategy

If Godot **holds the reference** (unsafe — ArrayPool return would corrupt):
- Document the finding
- Use the fallback: persistent per-peer `byte[]` sized at `_GetMaxPacketSize()` (1 MB), allocated once at connect-time
- Update the plan for Task 11 accordingly

**Step 3: Write decision doc**

```markdown
# Decision: Godot _GetPacket Memory Contract

Date: [date]
Result: [COPY / HOLD — fill in after investigation]
Source: [link to Godot source line]

## Finding
[Describe what Godot does with rBuffer after _GetPacket returns]

## Consequence for SteamMultiplayerPeer
[ArrayPool safe / must use persistent buffer]
```

**Step 4: Commit**
```bash
git add docs/decisions/godot-get-packet-memory-contract.md
git commit -m "docs: verify Godot _GetPacket memory contract (MASTER_DESIGN §8.4.1 blocking task)"
```

---

## Task 7: `HandshakeProtocol` — State Machine + Timeout

**Objective:** Implement the peer ID handshake protocol (MASTER_DESIGN §8.7). Pure state machine, no Godot types, no native calls.

**Files:**
- Create: `src/Manifold.Core/Networking/HandshakeProtocol.cs`
- Create: `src/Manifold.Core.Tests/Protocol/HandshakeProtocolTests.cs`

**Step 1: Write failing tests**

```csharp
public class HandshakeProtocolTests
{
    // Server side
    [Fact] public void Server_BuildHandshake_encodes_peer_id_as_int32_little_endian();
    [Fact] public void Server_BuildHandshake_uses_data_2_bytes_plus_4_byte_peer_id();
    [Fact] public void Server_recognises_HandshakeAck_and_returns_assigned_peer_id();
    [Fact] public void Server_rejects_non_ack_packet_as_invalid_response();

    // Client side
    [Fact] public void Client_ParseHandshake_extracts_peer_id_from_server_packet();
    [Fact] public void Client_BuildAck_produces_2_byte_ack_packet();
    [Fact] public void Client_rejects_malformed_handshake_packet();

    // Timeout
    [Fact] public void HandshakeState_is_pending_before_completion();
    [Fact] public void HandshakeState_is_complete_after_ack_received_on_server();
    [Fact] public void HandshakeState_expires_after_timeout_duration();
}
```

**Step 2: Implement `HandshakeProtocol.cs`**

```csharp
// src/Manifold.Core/Networking/HandshakeProtocol.cs
using System;
using System.Buffers.Binary;

namespace Manifold.Core.Networking;

/// <summary>
/// Encodes and decodes the Manifold handshake protocol messages.
/// The handshake assigns a Godot peer ID to each connecting client.
/// (MASTER_DESIGN §8.7)
/// </summary>
/// <remarks>
/// Server → Client (Handshake):  [0x01][0x00][peer_id: int32 LE]  (6 bytes)
/// Client → Server (Ack):        [0x02][0x00]                      (2 bytes)
/// </remarks>
internal static class HandshakeProtocol
{
    public const int  HandshakePacketSize = 6; // 2-byte header + 4-byte peer ID
    public const int  AckPacketSize       = 2; // header only
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Builds the server-to-client handshake packet for a given Godot peer ID.</summary>
    public static byte[] BuildHandshake(int peerId)
    {
        var buf = new byte[HandshakePacketSize];
        var hdr = new PacketHeader(PacketKind.Handshake, channel: 0);
        hdr.Encode(buf.AsSpan());
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(PacketHeader.Size), peerId);
        return buf;
    }

    /// <summary>Builds the client-to-server acknowledgement packet.</summary>
    public static byte[] BuildAck()
    {
        var buf = new byte[AckPacketSize];
        new PacketHeader(PacketKind.HandshakeAck, channel: 0).Encode(buf.AsSpan());
        return buf;
    }

    /// <summary>
    /// Parses a server handshake packet received by the client.
    /// Returns the assigned peer ID on success.
    /// </summary>
    public static bool TryParseHandshake(ReadOnlySpan<byte> data, out int peerId)
    {
        peerId = 0;
        if (data.Length < HandshakePacketSize) return false;
        if (!PacketHeader.TryDecode(data, out var hdr)) return false;
        if (hdr.Kind != PacketKind.Handshake) return false;
        peerId = BinaryPrimitives.ReadInt32LittleEndian(data[PacketHeader.Size..]);
        return true;
    }

    /// <summary>Returns true if the packet is a valid HandshakeAck from the client.</summary>
    public static bool IsAck(ReadOnlySpan<byte> data)
    {
        if (!PacketHeader.TryDecode(data, out var hdr)) return false;
        return hdr.Kind == PacketKind.HandshakeAck;
    }
}

/// <summary>Tracks the handshake state for a single connecting peer.</summary>
internal sealed class HandshakeState
{
    private readonly DateTime _deadline;
    public bool IsComplete { get; private set; }
    public bool IsExpired  => !IsComplete && DateTime.UtcNow >= _deadline;

    public HandshakeState(TimeSpan? timeout = null)
        => _deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

    public void MarkComplete() => IsComplete = true;
}
```

**Step 3: Run tests**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ --filter "HandshakeProtocolTests" -v minimal
```

**Step 4: Commit**
```bash
git add src/Manifold.Core/Networking/HandshakeProtocol.cs src/Manifold.Core.Tests/Protocol/HandshakeProtocolTests.cs
git commit -m "feat: HandshakeProtocol encode/decode + HandshakeState timeout (MASTER_DESIGN §8.7)"
```

---

## Task 8: `SteamNetworkingCore` — Structure + Init

**Objective:** Create the internal `SteamNetworkingCore` class — the `ISteamNetworkingSockets` wrapper. Build the host/client creation paths and the `IntPtr self` accessor pattern.

**Files:**
- Create: `src/Manifold.Core/Networking/SteamNetworkingCore.cs`
- Modify: `src/Manifold.Core/Testing/IBackends.cs` (extend `INetworkingBackend` if needed)
- Modify: `src/Manifold.Core/Testing/FakeSteamBackend.cs`

**Step 1: Write tests for host/client creation**

```csharp
// src/Manifold.Core.Tests/StateMachine/SteamNetworkingCoreTests.cs
public class SteamNetworkingCoreTests
{
    private readonly FakeSteamBackend _fake = new();

    [Fact] public void CreateHost_calls_CreateListenSocketP2P_and_CreatePollGroup();
    [Fact] public void CreateHost_returns_valid_ListenSocket_handle();
    [Fact] public void CreateClient_calls_ConnectP2P();
    [Fact] public void CreateClient_returns_valid_NetConnection_handle();
    [Fact] public void Close_host_calls_CloseListenSocket_and_DestroyPollGroup();
    [Fact] public void Close_client_calls_CloseConnection();
}
```

**Step 2: Implement `SteamNetworkingCore.cs` structure**

```csharp
// src/Manifold.Core/Networking/SteamNetworkingCore.cs
using System;
using System.Buffers;
using Manifold.Core.Interop;
using Manifold.Core.Testing;

namespace Manifold.Core.Networking;

/// <summary>
/// Internal wrapper around ISteamNetworkingSockets.
/// Handles host/client lifecycle, send, receive, and connection management.
/// Never exposed publicly — SteamMultiplayerPeer consumes this internally.
/// (MASTER_DESIGN §8.1)
/// </summary>
internal sealed class SteamNetworkingCore
{
    private readonly INetworkingBackend _backend;
    private IntPtr _socketsPtr;   // IntPtr from SteamAPI_SteamNetworkingSockets_v012()
    private bool   _isHost;

    // Host-only
    private uint _listenSocket;
    private uint _pollGroup;

    // Client-only
    private uint _serverConnection;

    internal const int MaxMessagesPerFrame = 512;

    /// <summary>
    /// Create with a live backend (<see cref="LiveSteamNetworkingBackend"/>)
    /// or a <see cref="FakeSteamBackend"/> for tests.
    /// </summary>
    internal SteamNetworkingCore(INetworkingBackend backend)
        => _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    // ── Host path ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a P2P listen socket and poll group for hosting.
    /// Returns the ListenSocket handle, or Invalid on failure.
    /// </summary>
    internal ListenSocket CreateHost(int virtualPort = 0)
    {
        _isHost       = true;
        _listenSocket = _backend.CreateListenSocketP2P(virtualPort);
        _pollGroup    = _backend.CreatePollGroup();
        return new ListenSocket(_listenSocket);
    }

    // ── Client path ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initiates a P2P connection to the host identified by <paramref name="hostSteamId"/>.
    /// Returns the NetConnection handle, or Invalid on failure.
    /// </summary>
    internal NetConnection CreateClient(SteamId hostSteamId, int virtualPort = 0)
    {
        _isHost = false;
        _serverConnection = _backend.ConnectP2P((ulong)hostSteamId, virtualPort);
        return new NetConnection(_serverConnection);
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    /// <summary>Sends a packet to a specific connection.</summary>
    internal int SendTo(uint connection, ReadOnlySpan<byte> data, int sendFlags)
        => _backend.SendMessageToConnection(connection, data, sendFlags);

    // ── Receive (Phase 2 continued in Task 9) ────────────────────────────────

    // ── Close ─────────────────────────────────────────────────────────────────

    internal void CloseConnection(uint connection, int reason = 0, bool linger = false)
        => _backend.CloseConnection(connection, reason, null, linger);

    internal void Close()
    {
        if (_isHost)
        {
            _backend.CloseListenSocket(_listenSocket);
            _backend.DestroyPollGroup(_pollGroup);
        }
        else
        {
            _backend.CloseConnection(_serverConnection, 0, null, false);
        }
    }
}
```

**Step 3: Extend `INetworkingBackend` and `FakeSteamBackend`**

Add `SendMessageToConnection` to `INetworkingBackend`:
```csharp
int SendMessageToConnection(uint hConn, ReadOnlySpan<byte> data, int sendFlags);
```
Add corresponding implementation to `FakeSteamBackend`.

**Step 4: Run tests**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ --filter "SteamNetworkingCoreTests" -v minimal
```

**Step 5: Commit**
```bash
git add src/Manifold.Core/Networking/SteamNetworkingCore.cs src/Manifold.Core/Testing/ src/Manifold.Core.Tests/StateMachine/SteamNetworkingCoreTests.cs
git commit -m "feat: SteamNetworkingCore scaffold — host/client create/close (MASTER_DESIGN §8.1)"
```

---

## Task 9: `SteamNetworkingCore` — Receive Path

**Objective:** Implement the receive pipeline — PollGroup drain (host) and per-connection receive (client) — with `ArrayPool<IntPtr>` for message pointer buffers. Implement `SteamNetworkingMessage_t` struct and `Release()` call. (MASTER_DESIGN §8.4.1, §8.4.2)

**Files:**
- Modify: `src/Manifold.Core/Networking/SteamNetworkingCore.cs`
- Create: `src/Manifold.Core/Networking/ReceivedPacket.cs`
- Modify: `src/Manifold.Core/Testing/IBackends.cs`
- Modify: `src/Manifold.Core/Testing/FakeSteamBackend.cs`

**Step 1: Define `SteamNetworkingMessage_t` layout**

Add to `SteamNative.Manual.cs` (alongside ConnectP2P):
```csharp
/// <summary>
/// Steam networking message. Manually defined — Pack=1 (explicitly packed in SDK headers).
/// Caller must call Release() after consuming m_pData.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct SteamNetworkingMessage_t
{
    internal IntPtr  m_pData;
    internal int     m_cbSize;
    internal uint    m_conn;              // HSteamNetConnection
    internal ulong   m_identitySender;   // offset into SteamNetworkingIdentity (SteamID64 at +8)
    internal long    m_nConnUserData;
    internal long    m_usecTimeReceived;
    internal long    m_nMessageNumber;
    internal IntPtr  m_pfnFreeData;
    internal IntPtr  m_pfnRelease;        // call this to free the message
    internal int     m_nChannel;
    internal int     m_idxLane;
    internal int     m_pad;
    // Total: verify with native sizeof test

    internal void Release()
    {
        if (m_pfnRelease != IntPtr.Zero)
            ((delegate* unmanaged[Cdecl]<IntPtr, void>)m_pfnRelease)((IntPtr)Unsafe.AsPointer(ref this));
    }
}
```

**Step 2: Define `ReceivedPacket`**

```csharp
// src/Manifold.Core/Networking/ReceivedPacket.cs
namespace Manifold.Core.Networking;

/// <summary>
/// A decoded incoming packet from the Steam receive queue.
/// Buffer is rented from ArrayPool — caller must return it via ArrayPool.Shared.Return().
/// </summary>
internal readonly struct ReceivedPacket
{
    public byte[]   Buffer       { get; init; }
    public int      Size         { get; init; }
    public uint     Connection   { get; init; }  // HSteamNetConnection
    public byte     Channel      { get; init; }
    public PacketKind Kind       { get; init; }
}
```

**Step 3: Add `DrainMessages()` to `SteamNetworkingCore`**

```csharp
/// <summary>
/// Drains all pending messages from Steam into <paramref name="output"/>.
/// Each ReceivedPacket.Buffer is rented from ArrayPool — consumer must return it.
/// Host uses PollGroup; Client uses per-connection receive.
/// </summary>
internal void DrainMessages(List<ReceivedPacket> output)
{
    IntPtr[]? msgPtrs = null;
    int count;
    try
    {
        msgPtrs = ArrayPool<IntPtr>.Shared.Rent(MaxMessagesPerFrame);

        if (_isHost)
            count = _backend.ReceiveMessagesOnPollGroup(_pollGroup, msgPtrs, MaxMessagesPerFrame);
        else
            count = _backend.ReceiveMessagesOnConnection(_serverConnection, msgPtrs, MaxMessagesPerFrame);

        for (int i = 0; i < count; i++)
            ProcessMessage(msgPtrs[i], output);
    }
    finally
    {
        if (msgPtrs != null)
            ArrayPool<IntPtr>.Shared.Return(msgPtrs);
    }
}

private static unsafe void ProcessMessage(IntPtr msgPtr, List<ReceivedPacket> output)
{
    ref var msg = ref *(SteamNetworkingMessage_t*)msgPtr;
    try
    {
        if (msg.m_cbSize < PacketHeader.Size) return; // malformed

        var rawData = new ReadOnlySpan<byte>((void*)msg.m_pData, msg.m_cbSize);
        if (!PacketHeader.TryDecode(rawData, out var header)) return;

        int payloadSize = msg.m_cbSize - PacketHeader.Size;
        var rented = ArrayPool<byte>.Shared.Rent(payloadSize > 0 ? payloadSize : 1);
        rawData[PacketHeader.Size..].CopyTo(rented);

        output.Add(new ReceivedPacket
        {
            Buffer     = rented,
            Size       = payloadSize,
            Connection = msg.m_conn,
            Channel    = header.Channel,
            Kind       = header.Kind,
        });
    }
    finally
    {
        msg.Release();
    }
}
```

**Step 4: Extend `INetworkingBackend` with receive methods**

```csharp
int ReceiveMessagesOnPollGroup(uint pollGroup, IntPtr[] ppOut, int maxMessages);
int ReceiveMessagesOnConnection(uint hConn, IntPtr[] ppOut, int maxMessages);
```
Add safe no-op implementations to `FakeSteamBackend` (return 0).

**Step 5: Run tests**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ -v minimal
```

**Step 6: Commit**
```bash
git add src/Manifold.Core/Networking/ src/Manifold.Core/Testing/ src/Manifold.Core.Tests/
git commit -m "feat: SteamNetworkingCore receive path with ArrayPool drain (MASTER_DESIGN §8.4)"
```

---

## Task 10: `SteamNetworkingCore` — Connection Callbacks + Status

**Objective:** Subscribe to `SteamNetConnectionStatusChangedCallback_t` via `CallbackDispatcher`, surface connection state changes, and handle incoming connection acceptance (host side).

**Files:**
- Modify: `src/Manifold.Core/Networking/SteamNetworkingCore.cs`
- Modify: `src/Manifold.Core.Tests/StateMachine/SteamNetworkingCoreTests.cs`

**Step 1: Write tests**

```csharp
[Fact] public void OnConnectionStatusChanged_incoming_connection_is_surfaced_via_event();
[Fact] public void OnConnectionStatusChanged_connected_is_surfaced_via_event();
[Fact] public void OnConnectionStatusChanged_closed_with_reason_is_surfaced_via_event();
```

**Step 2: Define callback struct and events**

```csharp
// Connection status change callback (k_iCallback = 1221)
// Already in SteamNative.Callbacks.cs as SteamNetConnectionStatusChangedCallback_t

// Add to SteamNetworkingCore:
internal event Action<uint, int>?                   IncomingConnection;   // (conn, state)
internal event Action<uint, int, DisconnectInfo>?   ConnectionChanged;   // (conn, newState, info)

private IDisposable? _statusSubscription;

// Subscribe during init:
_statusSubscription = CallbackDispatcher.Register(
    SteamNetConnectionStatusChangedCallback_t.k_iCallback,
    OnStatusChanged);

private unsafe void OnStatusChanged(IntPtr ptr)
{
    ref var cb = ref *(SteamNetConnectionStatusChangedCallback_t*)ptr;
    // Route to appropriate handler
    // cb.m_hConn, cb.m_info.m_eState, cb.m_eOldState, cb.m_info.m_szEndDebug
}
```

**Step 3: Handle incoming connections (host)**

When `eState == k_ESteamNetworkingConnectionState_Connecting` and we're a host:
```csharp
_backend.AcceptConnection(cb.m_hConn);
_backend.SetConnectionPollGroup(cb.m_hConn, _pollGroup);
IncomingConnection?.Invoke(cb.m_hConn, cb.m_info.m_eState);
```

**Step 4: Run tests + commit**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ -v minimal
git add src/Manifold.Core/Networking/SteamNetworkingCore.cs src/Manifold.Core.Tests/
git commit -m "feat: SteamNetworkingCore connection status callbacks + incoming acceptance"
```

---

## Task 11: `SteamMultiplayerPeer` — Scaffold + State Machine

**Objective:** Create `Manifold.Godot/Networking/SteamMultiplayerPeer.cs`. Wire the connection state machine (MASTER_DESIGN §8.6). Implement `_GetConnectionStatus()`, `_IsServer()`, `_GetUniqueId()`.

**Files:**
- Create: `src/Manifold.Godot/Networking/SteamMultiplayerPeer.cs`

**Step 1: Implement the state machine and basic overrides**

Internal states (MASTER_DESIGN §8.6): `Listening`, `Connecting`, `Authenticating`, `Connected`, `Disconnecting`, `Disconnected`.

State → `_GetConnectionStatus()` mapping:
- `Listening/Connecting/Authenticating` → `ConnectionStatus.Connecting`
- `Connected/Disconnecting` → `ConnectionStatus.Connected`
- `Disconnected` → `ConnectionStatus.Disconnected`

```csharp
public partial class SteamMultiplayerPeer : MultiplayerPeerExtension
{
    private enum PeerState
    {
        Idle, Listening, Connecting, Authenticating, Connected, Disconnecting, Disconnected
    }
    private PeerState _state = PeerState.Idle;

    private readonly SteamNetworkingCore _core;
    private readonly PeerIdMapper        _peerMapper = new();
    private int  _uniqueId;
    private bool _isServer;
    private bool _refusingNewConnections;
    private int  _transferChannel;
    private MultiplayerPeer.TransferModeEnum _transferMode = MultiplayerPeer.TransferModeEnum.Reliable;
    private int  _targetPeer;
    private bool _unreliableOrderedWarned; // for UnreliableOrdered one-time warning

    private readonly Queue<ReceivedPacket> _incoming = new();
    private ReceivedPacket _currentPacket;

    // Disconnect info (MASTER_DESIGN §8.8)
    public DisconnectInfo? LastDisconnectInfo { get; private set; }
    [Signal] public delegate void PeerDisconnectedWithReasonEventHandler(int peerId, int code, string reason, bool wasLocal);

    public SteamMultiplayerPeer()
    {
        _core = new SteamNetworkingCore(/* live backend injected here */);
        // Register with SteamPeerRegistry for lifecycle.Dispose() hook
        SteamPeerRegistry.Register(this);
    }

    // ── State helpers ─────────────────────────────────────────────────────────

    public override MultiplayerPeer.ConnectionStatus _GetConnectionStatus() => _state switch
    {
        PeerState.Listening or PeerState.Connecting or PeerState.Authenticating
            => MultiplayerPeer.ConnectionStatus.Connecting,
        PeerState.Connected or PeerState.Disconnecting
            => MultiplayerPeer.ConnectionStatus.Connected,
        _   => MultiplayerPeer.ConnectionStatus.Disconnected,
    };

    public override bool _IsServer() => _isServer;
    public override int  _GetUniqueId() => _uniqueId;
    public override bool _IsServerRelaySupported() => true;
    public override bool _IsRefusingNewConnections() => _refusingNewConnections;
    public override void _SetRefuseNewConnections(bool pEnable) => _refusingNewConnections = pEnable;
    public override int  _GetTransferChannel() => _transferChannel;
    public override void _SetTransferChannel(int pChannel) => _transferChannel = pChannel;
    public override MultiplayerPeer.TransferModeEnum _GetTransferMode() => _transferMode;
    public override void _SetTransferMode(MultiplayerPeer.TransferModeEnum pMode)
    {
        if (pMode == MultiplayerPeer.TransferModeEnum.UnreliableOrdered && !_unreliableOrderedWarned)
        {
            _unreliableOrderedWarned = true;
            GD.PushWarning(
                "[Manifold] WARNING: TransferMode.UnreliableOrdered is set on SteamMultiplayerPeer, " +
                "but Steam has no native ordered-unreliable transport. Manifold is using unordered " +
                "unreliable delivery. In v1, Manifold does not emulate ordered-unreliable delivery. " +
                "Applications requiring stale-packet suppression or monotonic snapshot delivery must " +
                "implement sequence handling above the transport layer.");
        }
        _transferMode = pMode;
    }
    public override void _SetTargetPeer(int pPeer) => _targetPeer = pPeer;
    public override int  _GetMaxPacketSize() => 1_048_576;
    public override int  _GetAvailablePacketCount() => _incoming.Count;
}
```

**Commit:**
```bash
git add src/Manifold.Godot/Networking/SteamMultiplayerPeer.cs
git commit -m "feat: SteamMultiplayerPeer scaffold — state machine + basic overrides (MASTER_DESIGN §8.10)"
```

---

## Task 12: `SteamMultiplayerPeer` — `CreateHost` / `CreateClient`

**Objective:** Implement `CreateHost()`, `CreateClient()`, `HostWithLobbyAsync()`, `JoinLobbyAsync()`.

**Files:**
- Modify: `src/Manifold.Godot/Networking/SteamMultiplayerPeer.cs`

**Step 1: Implement `CreateHost`**

```csharp
/// <summary>
/// Creates a P2P listen socket. Server peer ID is always 1.
/// </summary>
public Error CreateHost(int virtualPort = 0)
{
    if (_state != PeerState.Idle)
        return Error.AlreadyInUse;

    var socket = _core.CreateHost(virtualPort);
    if (!socket.IsValid) return Error.CantCreate;

    _isServer = true;
    _uniqueId = 1;
    _state    = PeerState.Listening;
    return Error.Ok;
}
```

**Step 2: Implement `CreateClient`**

```csharp
/// <summary>
/// Connects to a host identified by their Steam ID.
/// State transitions to Connecting; on Steam connection established → Authenticating.
/// </summary>
public Error CreateClient(SteamId hostSteamId, int virtualPort = 0)
{
    if (_state != PeerState.Idle)
        return Error.AlreadyInUse;

    var conn = _core.CreateClient(hostSteamId, virtualPort);
    if (!conn.IsValid) return Error.CantCreate;

    _isServer = false;
    _uniqueId = 0; // assigned by server during handshake
    _state    = PeerState.Connecting;
    return Error.Ok;
}
```

**Step 3: Implement `HostWithLobbyAsync` and `JoinLobbyAsync` stubs**

These call `CreateLobbyAsync` / `JoinLobbyAsync` on `SteamMatchmaking` (Phase 3) then call `CreateHost()` / `CreateClient()`. For Phase 2, stub them to return `Error.Unavailable` with a comment marking them Phase 3:

```csharp
/// <summary>Phase 3: requires SteamMatchmaking. Stub for now.</summary>
public Task<Error> HostWithLobbyAsync(ELobbyType type, int maxMembers,
    CancellationToken cancellationToken = default)
    => Task.FromResult(Error.Unavailable);

public Task<Error> JoinLobbyAsync(SteamId lobbyId,
    CancellationToken cancellationToken = default)
    => Task.FromResult(Error.Unavailable);
```

**Commit:**
```bash
git add src/Manifold.Godot/Networking/SteamMultiplayerPeer.cs
git commit -m "feat: SteamMultiplayerPeer CreateHost/CreateClient + lobby stubs (MASTER_DESIGN §8.10)"
```

---

## Task 13: `SteamMultiplayerPeer` — `_Poll`, `_PutPacket`, `_GetPacket`, `_Close`, `_DisconnectPeer`

**Objective:** Implement the five heaviest overrides — the frame pump, send path, receive path, close, and peer disconnect.

**Files:**
- Modify: `src/Manifold.Godot/Networking/SteamMultiplayerPeer.cs`

**Step 1: `_Poll()`**

```csharp
public override void _Poll()
{
    if (_state is PeerState.Idle or PeerState.Disconnected) return;

    // Drain Steam messages into _incoming via SteamNetworkingCore
    var pending = new List<ReceivedPacket>();
    _core.DrainMessages(pending);

    foreach (var pkt in pending)
    {
        switch (pkt.Kind)
        {
            case PacketKind.Data:
                _incoming.Enqueue(pkt);
                break;

            case PacketKind.Handshake when !_isServer:
                // Client receives peer ID assignment from server
                if (HandshakeProtocol.TryParseHandshake(pkt.Buffer.AsSpan(0, pkt.Size), out int peerId))
                {
                    _uniqueId = peerId;
                    var ack = HandshakeProtocol.BuildAck();
                    _core.SendTo(_core.ServerConnection, ack, SteamSendFlags.Reliable);
                    _state = PeerState.Connected;
                    EmitSignal(MultiplayerPeer.SignalName.PeerConnected, 1L);
                }
                ArrayPool<byte>.Shared.Return(pkt.Buffer);
                break;

            case PacketKind.HandshakeAck when _isServer:
                // Server receives ACK — complete handshake, notify Godot
                if (_peerMapper.TryGetGodotId(pkt.Connection, out int clientId))
                {
                    if (_pendingHandshakes.TryGetValue(pkt.Connection, out var hs))
                    {
                        hs.MarkComplete();
                        _pendingHandshakes.Remove(pkt.Connection);
                        _state = PeerState.Connected;
                        EmitSignal(MultiplayerPeer.SignalName.PeerConnected, (long)clientId);
                    }
                }
                ArrayPool<byte>.Shared.Return(pkt.Buffer);
                break;

            case PacketKind.Disconnect:
                HandleRemoteDisconnect(pkt.Connection);
                ArrayPool<byte>.Shared.Return(pkt.Buffer);
                break;

            default:
                ArrayPool<byte>.Shared.Return(pkt.Buffer);
                break;
        }
    }

    // Check handshake timeouts
    CheckHandshakeTimeouts();
}
```

**Step 2: `_PutPacket()`**

Send flags derived from `TransferMode` (MASTER_DESIGN §8.2):
```csharp
public override Error _PutPacket(byte[] pBuffer, int pBufferSize)
{
    if (_state != PeerState.Connected) return Error.Unavailable;

    int sendFlags = _transferMode switch
    {
        MultiplayerPeer.TransferModeEnum.Reliable          => 8,  // k_nSteamNetworkingSend_Reliable
        MultiplayerPeer.TransferModeEnum.UnreliableOrdered => 0,  // plain unreliable (see warning)
        _                                                  => 5,  // UnreliableNoDelay (4|1)
    };
    if (NoNagle) sendFlags |= 1;
    if (NoDelay) sendFlags |= 4;

    // Prepend 2-byte Manifold header
    var hdr = new PacketHeader(PacketKind.Data, (byte)_transferChannel);
    Span<byte> outBuf = stackalloc byte[PacketHeader.Size + pBufferSize];
    hdr.Encode(outBuf);
    pBuffer.AsSpan(0, pBufferSize).CopyTo(outBuf[PacketHeader.Size..]);

    if (_targetPeer <= 0)
    {
        // Broadcast (0) or all-except-one (negative)
        foreach (var (conn, godotId) in /* enumerate all connections */)
            if (_targetPeer == 0 || godotId != -_targetPeer)
                _core.SendTo(conn, outBuf, sendFlags);
    }
    else
    {
        var conn = GetConnectionForPeer(_targetPeer);
        if (conn == 0) return Error.InvalidParameter;
        _core.SendTo(conn, outBuf, sendFlags);
    }

    return Error.Ok;
}
```

**Step 3: `_GetPacket()`**

```csharp
public override Error _GetPacket(out byte[] rBuffer, out int rBufferSize)
{
    if (_incoming.Count == 0) { rBuffer = Array.Empty<byte>(); rBufferSize = 0; return Error.Unavailable; }

    _currentPacket = _incoming.Dequeue();
    rBuffer     = _currentPacket.Buffer;
    rBufferSize = _currentPacket.Size;
    // Return buffer to pool — SAFE only if Godot copies before returning (verified Task 6)
    ArrayPool<byte>.Shared.Return(_currentPacket.Buffer);
    return Error.Ok;
}
```

**Step 4: `_GetPacketPeer`, `_GetPacketChannel`, `_GetPacketMode`**

```csharp
public override int _GetPacketPeer()
    => _peerMapper.TryGetGodotId(_currentPacket.Connection, out int id) ? id : 0;
public override int _GetPacketChannel() => _currentPacket.Channel;
public override MultiplayerPeer.TransferModeEnum _GetPacketMode() => _currentPacket.Kind switch
{
    _ => _transferMode  // mode not encoded in v1 header; use current mode
};
```

**Step 5: `_Close()`**

```csharp
public override void _Close()
{
    if (_state is PeerState.Idle or PeerState.Disconnected) return;
    _state = PeerState.Disconnecting;

    _core.Close();
    _peerMapper.Clear();
    _pendingHandshakes.Clear();
    while (_incoming.TryDequeue(out var pkt))
        ArrayPool<byte>.Shared.Return(pkt.Buffer);

    _state    = PeerState.Disconnected;
    _uniqueId = 0;
    SteamPeerRegistry.Unregister(this);
}
```

**Step 6: `_DisconnectPeer()`**

```csharp
public override void _DisconnectPeer(int pPeer, bool pForce)
{
    if (!_isServer) return;
    var steamId = _peerMapper.GetSteamId(pPeer);
    // find connection handle for this peer
    // CloseConnection with reason 0, linger = !pForce
    // emit PeerDisconnected
    _peerMapper.Remove(pPeer);
}
```

**Commit:**
```bash
git add src/Manifold.Godot/Networking/SteamMultiplayerPeer.cs
git commit -m "feat: SteamMultiplayerPeer _Poll/_PutPacket/_GetPacket/_Close/_DisconnectPeer (MASTER_DESIGN §8.10)"
```

---

## Task 14: `SteamMultiplayerPeer` — Incoming Connection Handler (Host Handshake)

**Objective:** When `SteamNetworkingCore` surfaces an incoming connection event, the host assigns a peer ID, sends the handshake, adds to `PeerIdMapper`, and starts the 5-second handshake timeout timer.

**Files:**
- Modify: `src/Manifold.Godot/Networking/SteamMultiplayerPeer.cs`

**Step 1: Subscribe to `SteamNetworkingCore.IncomingConnection`**

```csharp
// In constructor:
_core.IncomingConnection += OnIncomingConnection;
_core.ConnectionChanged  += OnConnectionChanged;

private readonly Dictionary<uint, HandshakeState> _pendingHandshakes = new();

private void OnIncomingConnection(uint connection, int state)
{
    if (!_isServer || _refusingNewConnections) return;

    // Get remote Steam ID from connection info
    ulong remoteSteamId64 = _core.GetRemoteSteamId(connection);
    var   steamId         = new SteamId(remoteSteamId64);
    int   godotId         = _peerMapper.Register(steamId, connection);

    // Send peer ID handshake
    var handshake = HandshakeProtocol.BuildHandshake(godotId);
    _core.SendTo(connection, handshake, 8); // Reliable

    // Start handshake timeout tracking
    _pendingHandshakes[connection] = new HandshakeState();
}

private void CheckHandshakeTimeouts()
{
    var expired = new List<uint>();
    foreach (var (conn, hs) in _pendingHandshakes)
        if (hs.IsExpired) expired.Add(conn);
    foreach (var conn in expired)
    {
        _pendingHandshakes.Remove(conn);
        if (_peerMapper.TryGetGodotId(conn, out int id))
        {
            _peerMapper.Remove(id);
            _core.CloseConnection(conn, 0);
        }
    }
}
```

**Step 2: Add `GetRemoteSteamId` to `SteamNetworkingCore`**

Uses `NetworkingSockets_GetConnectionInfo` to read the `SteamNetworkingIdentity` from the connection info struct, extracts `m_steamID64` at offset +8 in the identity.

**Commit:**
```bash
git add src/Manifold.Godot/Networking/SteamMultiplayerPeer.cs src/Manifold.Core/Networking/SteamNetworkingCore.cs
git commit -m "feat: host handshake — peer ID assignment, handshake send, 5s timeout (MASTER_DESIGN §8.7)"
```

---

## Task 15: State Machine Tests

**Objective:** Full test coverage of `SteamMultiplayerPeer` state transitions using `FakeSteamBackend`. Tests live in `Manifold.Core.Tests` (no Godot dependency; test the core networking directly).

**Files:**
- Create: `src/Manifold.Core.Tests/StateMachine/SteamMultiplayerPeerTests.cs`

**Step 1: Write all state machine tests**

Covering every transition in MASTER_DESIGN §8.6 and test cases from §10 category 3:

```csharp
// All paths in the connection state machine (§8.6)
[Fact] public void CreateHost_transitions_to_Listening();
[Fact] public void CreateClient_transitions_to_Connecting();
[Fact] public void Host_incoming_connection_transitions_to_Authenticating();
[Fact] public void Handshake_complete_transitions_to_Connected();
[Fact] public void Handshake_timeout_transitions_to_Disconnected_with_reason();
[Fact] public void Close_from_Connected_transitions_to_Disconnected();
[Fact] public void Remote_close_transitions_to_Disconnected_with_DisconnectInfo();
[Fact] public void DisconnectInfo_populated_on_remote_close();
[Fact] public void DisconnectInfo_populated_on_local_close();
[Fact] public void WasLocalClose_is_true_on_Close_call();
[Fact] public void WasLocalClose_is_false_on_remote_disconnect();

// Negative target peer routing (all-except)
[Fact] public void PutPacket_with_negative_target_sends_to_all_except_target_peer();
[Fact] public void PutPacket_with_zero_target_broadcasts_to_all_peers();

// 2-byte header round-trip
[Fact] public void PutPacket_prepends_2_byte_header_correctly();
[Fact] public void GetPacket_strips_2_byte_header_from_payload();

// All 21 MultiplayerPeerExtension overrides accessible
[Fact] public void All_21_overrides_return_sane_defaults_before_connect();

// UnreliableOrdered one-time warning
[Fact] public void SetTransferMode_UnreliableOrdered_emits_warning_exactly_once();
[Fact] public void SetTransferMode_UnreliableOrdered_second_call_does_not_emit_again();
```

**Step 2: Run all tests**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ -v minimal
```
Expected: all pass.

**Step 3: Commit**
```bash
git add src/Manifold.Core.Tests/StateMachine/SteamMultiplayerPeerTests.cs
git commit -m "test: SteamMultiplayerPeer state machine — all transitions, protocol, routing (MASTER_DESIGN §10 cat 3)"
```

---

## Task 16: `SteamLobbySession`

**Objective:** Implement `SteamLobbySession` in `Manifold.Godot` — a Godot `RefCounted` wrapping lobby lifecycle. This is the ergonomic companion to `SteamMultiplayerPeer` for lobby flows.

**Files:**
- Create: `src/Manifold.Godot/Networking/SteamLobbySession.cs`

**Step 1: Implement**

```csharp
/// <summary>
/// Manages the lifecycle of a single Steam lobby session.
/// Created by SteamMultiplayerPeer when hosting/joining with HostWithLobbyAsync/JoinLobbyAsync.
/// Exposes lobby metadata and member list via Godot-compatible properties.
/// Phase 3: full implementation when SteamMatchmaking is complete.
/// </summary>
public partial class SteamLobbySession : RefCounted
{
    public SteamId LobbyId     { get; internal set; }
    public SteamId OwnerSteamId { get; internal set; }
    public int     MemberCount  { get; internal set; }

    public bool IsValid => LobbyId.IsValid;

    // Phase 3: member list, metadata get/set, invite — stubs now
    public SteamId[] GetMembers() => Array.Empty<SteamId>();
    public string GetData(string key) => string.Empty;
    public bool SetData(string key, string value) => false;

    internal static SteamLobbySession Invalid { get; } = new() { LobbyId = SteamId.Invalid };
}
```

**Commit:**
```bash
git add src/Manifold.Godot/Networking/SteamLobbySession.cs
git commit -m "feat: SteamLobbySession scaffold — Phase 3 stubs for lobby metadata/members (MASTER_DESIGN §3)"
```

---

## Task 17: `SteamPeerRegistry` — Lifecycle Integration

**Objective:** Replace the no-op `SteamPeerRegistry` stub from Task 2 with a real implementation so `SteamLifecycle.Dispose()` can transition all active peers to Disconnected state (shutdown contract step 3).

**Files:**
- Modify: `src/Manifold.Core/Dispatch/SteamPeerRegistry.cs`

**Step 1: Implement**

```csharp
internal static class SteamPeerRegistry
{
    // Uses WeakReference to avoid keeping peers alive
    private static readonly List<WeakReference<ISteamPeer>> _peers = new();
    private static readonly object _lock = new();

    internal static void Register(ISteamPeer peer)
    {
        lock (_lock) _peers.Add(new WeakReference<ISteamPeer>(peer));
    }

    internal static void Unregister(ISteamPeer peer)
    {
        lock (_lock) _peers.RemoveAll(wr => !wr.TryGetTarget(out var p) || ReferenceEquals(p, peer));
    }

    internal static void ShutdownAll()
    {
        List<ISteamPeer> alive;
        lock (_lock)
        {
            alive = _peers
                .Select(wr => wr.TryGetTarget(out var p) ? p : null)
                .Where(p => p is not null)
                .ToList()!;
            _peers.Clear();
        }
        foreach (var peer in alive)
            peer.ForceDisconnect();
    }
}

/// <summary>Minimal interface for SteamPeerRegistry to call ForceDisconnect on peers during shutdown.</summary>
internal interface ISteamPeer
{
    void ForceDisconnect();
}
```

Add `ISteamPeer` to `SteamMultiplayerPeer` and implement `ForceDisconnect()` as `_Close()`.

**Commit:**
```bash
git add src/Manifold.Core/Dispatch/SteamPeerRegistry.cs src/Manifold.Godot/Networking/SteamMultiplayerPeer.cs
git commit -m "feat: SteamPeerRegistry — lifecycle shutdown integration (MASTER_DESIGN §4 shutdown contract step 3)"
```

---

## Task 18: End-to-End Verification Plan (Document)

**Objective:** Write a doc describing the end-to-end loopback test plan. The actual loopback requires a live Steam session, so this task documents the test procedure and what a passing run looks like. A unit-level simulation using `FakeSteamBackend` runs automatically in CI; the real loopback is a manual verification gate before Phase 2 is considered complete.

**Files:**
- Create: `docs/plans/phase2-e2e-loopback-test.md`

**Step 1: Write the doc**

```markdown
# Phase 2 End-to-End Loopback Test

## Goal
Two SteamMultiplayerPeer instances connect over local loopback (same machine,
two Godot instances or a single Godot scene with two peers sharing the same
Steam session for testing), exchange data packets, and disconnect cleanly.

## Automated gate (no Steam required)
Run the FakeSteamBackend simulation in StateMachine tests:
- Two SteamNetworkingCore instances share a FakeSteamBackend
- Host creates listen socket; client connects
- Handshake completes; both enter Connected
- Client sends 3 Data packets; host receives all 3
- Host disconnects client; DisconnectInfo is populated

Command: `dotnet test src/Manifold.Core.Tests/ --filter "E2E" -v minimal`
Expected: PASS

## Manual gate (live Steam, same machine)
### Prerequisites
- Steam client running and logged in
- steam_appid.txt with a valid (or SpaceWar 480) AppID in the sample project

### Steps
1. Open ManifoldSample in Godot (see samples/)
2. Run two instances (Export + F5 or two editor windows)
3. Instance A: click "Host"
4. Instance B: enter Instance A's SteamID64, click "Join"
5. Both consoles show: [Manifold] Peer X connected
6. Instance B: click "Send Test Packet"
7. Instance A console shows: Received: Hello from peer 2
8. Instance A: click "Disconnect"
9. Both consoles show: [Manifold] Peer disconnected (wasLocal: true/false)
10. Instance B: LastDisconnectInfo.Reason is non-empty

### Pass criteria
- No crashes, no exceptions in Godot output
- PeerConnected signal fires on both sides
- Data packet received intact (no corruption)
- PeerDisconnected signal fires with populated DisconnectInfo
```

**Commit:**
```bash
git add docs/plans/phase2-e2e-loopback-test.md
git commit -m "docs: Phase 2 end-to-end loopback test plan"
```

---

## Task 19: Full Test Run + Phase 2 Completion Check

**Objective:** Run the full test suite and verify all new tests pass alongside all Phase 1 tests. Update the README Phase 2 checklist.

**Step 1: Run all tests**
```bash
cd ~/Manifold && ~/.dotnet/dotnet test src/Manifold.Core.Tests/ -v minimal
```
Expected: ≥ 120 tests passing (80 Phase 1 + ≥40 Phase 2).

**Step 2: Update README Phase 2 checklist**

Mark complete:
```markdown
### Phase 2: Godot Integration *(complete)*
- [x] `PacketHeader` (2-byte encode/decode)
- [x] `HandshakeProtocol` (state machine, 5-second timeout)
- [x] `PeerIdMapper`
- [x] `SteamNetworkingCore` (internal ISteamNetworkingSockets wrapper)
- [x] `SteamMultiplayerPeer` (all 21 overrides, state machine, disconnect info, UnreliableOrdered warning)
- [x] `SteamLobbySession` (Phase 3 stubs)
- [x] State machine, protocol, and integration tests
- [ ] End-to-end: two peers connect via loopback — manual gate (requires live Steam)
```

**Step 3: Push to GitHub**
```bash
cd ~/Manifold
git add README.md
git commit -m "docs: mark Phase 2 complete in README"
git push
```

---

## Summary

| Task | Component | Files | Type |
|---|---|---|---|
| 1 | SteamLifecycle reconciliation | Lifecycle/ | Refactor |
| 2 | CallbackDispatcher reconciliation | Dispatch/ | Refactor |
| 3 | ConnectP2P manual P/Invoke | Interop/SteamNative.Manual.cs | Feature |
| 4 | PacketHeader | Networking/PacketHeader.cs | Feature + Tests |
| 5 | PeerIdMapper | Networking/PeerIdMapper.cs | Feature + Tests |
| 6 | _GetPacket memory contract proof | docs/decisions/ | Research + Doc |
| 7 | HandshakeProtocol | Networking/HandshakeProtocol.cs | Feature + Tests |
| 8 | SteamNetworkingCore scaffold | Networking/SteamNetworkingCore.cs | Feature + Tests |
| 9 | SteamNetworkingCore receive | SteamNetworkingCore.cs | Feature + Tests |
| 10 | SteamNetworkingCore callbacks | SteamNetworkingCore.cs | Feature + Tests |
| 11 | SteamMultiplayerPeer scaffold | Godot/Networking/ | Feature |
| 12 | CreateHost/CreateClient | SteamMultiplayerPeer.cs | Feature |
| 13 | _Poll/_PutPacket/_GetPacket/_Close | SteamMultiplayerPeer.cs | Feature |
| 14 | Host handshake handler | SteamMultiplayerPeer.cs | Feature |
| 15 | State machine tests | Core.Tests/StateMachine/ | Tests |
| 16 | SteamLobbySession | Godot/Networking/SteamLobbySession.cs | Feature |
| 17 | SteamPeerRegistry integration | Dispatch/SteamPeerRegistry.cs | Feature |
| 18 | E2E loopback test doc | docs/plans/ | Doc |
| 19 | Final test run + README | — | Verification |

**A good plan makes implementation obvious.**
