# Manifold

A C# Steamworks SDK library for Godot 4 that talks directly to Steam, with no man in the middle!

```csharp
var peer = new SteamMultiplayerPeer();
await peer.HostAsync(ELobbyType.FriendsOnly, maxMembers: 4);
Multiplayer.MultiplayerPeer = peer;
// @rpc just works from here.
```

[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE) [![.NET 8](https://img.shields.io/badge/.NET-8-purple)](https://dotnet.microsoft.com) [![Steamworks SDK](https://img.shields.io/badge/Steamworks%20SDK-1.64-informational)](https://partner.steamgames.com) [![Tests](https://img.shields.io/badge/tests-201%20passing-brightgreen)]() [![Status](https://img.shields.io/badge/status-WIP-orange)]()

---

## Why Manifold?

There are already great options in this space. Steamworks.NET is a solid general-purpose wrapper, and GodotSteam has a strong community behind it. Manifold fills a specific gap: a C# library that integrates naturally with Godot 4's multiplayer system, without depending on either of them.

A few things that shaped how it was built:

- **Direct P/Invoke.** No C++ extension sitting between your code and Steam. Fewer moving parts, fewer things to go wrong.
- **Auto-generated bindings.** The entire P/Invoke surface is generated from the `steam_api.json` file that ships with the SDK. Keeping up with SDK updates is a codegen run, not a manual porting job.
- **Real async/await.** Call results return proper `Task<T>` with cancellation and timeout support baked in.
- **Struct layout validation.** Packing is verified against the real SDK headers at build time, so layout bugs get caught before they cause silent data corruption at runtime.

---

## Packages

| Package | Description |
|---|---|
| `Manifold.Core` | Engine-agnostic. No Godot dependency. Works in servers, tests, anywhere .NET runs. |
| `Manifold.Godot` | Godot 4 integration. `SteamMultiplayerPeer`, `SteamManager` autoload, signal wrappers. *(Not yet released)* |

---

## Requirements

- [Godot 4.3+](https://godotengine.org/) (.NET build)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Steamworks SDK 1.64](https://partner.steamgames.com/doc/sdk)

> **Note:** While we're pre-NuGet, you'll need to supply the native Steam libraries (`steam_api64.dll` / `libsteam_api.so`) manually. Once the NuGet package ships in Phase 3, they'll be bundled automatically.

---

## Getting Started

The NuGet package hasn't been published yet. For now, clone the repo and reference the projects directly:

```bash
git clone https://github.com/TheDevRatt/Manifold.git
```

Then add a project reference to `Manifold.Core` (and `Manifold.Godot` once it's ready) from your `.csproj`:

```xml
<ProjectReference Include="../Manifold/src/Manifold.Core/Manifold.Core.csproj" />
```

Drop the Steamworks SDK native libraries into your project's output directory. A NuGet release and proper setup guide will come in Phase 3.

---

## Roadmap

### Phase 1: Foundation ✅

- [x] Source generator (ManifoldGen): reads `steam_api.json`, emits full P/Invoke layer
- [x] `SteamLifecycle`: init/shutdown, background callback pump, fatal error handling
- [x] `CallbackDispatcher`: type-safe subscriptions with disposable tokens
- [x] `CallResultAwaiter<T>`: async Steam call results with cancellation and timeout
- [x] Native struct size validator: catches packing bugs against real SDK headers at build time
- [x] 80 contract tests

### Phase 2: Networking Core *(implementation complete — pending live Steam validation)*

> **Status:** The networking primitives are fully implemented and all known correctness bugs have been fixed. The production Steam backend is not yet wired into `SteamMultiplayerPeer`'s public constructor — that happens in Phase 3 when `SteamLifecycle` is integrated. The manual E2E loopback test (requires a live Steam session) is the remaining gate.

- [x] `PacketHeader` (2-byte encode/decode, version + kind + channel)
- [x] `HandshakeProtocol` (server→client peer ID assignment, 5 s monotonic timeout via `TickCount64`)
- [x] `PeerIdMapper` (bidirectional SteamId ↔ connection ↔ Godot ID, ID wrap-around guard)
- [x] `SteamNetworkingCore` (internal `ISteamNetworkingSockets` wrapper, receive pipeline, `IDisposable`)
- [x] `SteamMultiplayerPeer` (all 21 overrides, full state machine, disconnect info, `UnreliableOrdered` warning, 524 KB packet cap, send-result logging)
- [x] `SteamLobbySession` scaffold (Phase 3 stubs)
- [x] `SteamPeerRegistry` + `ISteamPeer` (lifecycle shutdown integration)
- [x] `LiveSteamInit.GetHSteamPipe` uses correct `SteamAPI_GetHSteamPipe()` (not `HSteamUser`)
- [x] `CallResultAwaiter` idempotent/thread-safe `Dispose()`
- [x] `ProblemDetectedLocally` correctly reported as `WasLocalClose = true`
- [x] Unit + integration tests (201 passing via `FakeSteamBackend`)
- [x] `PacketHeader.TryDecode` rejects unknown kinds (0x4–0xF) and non-zero version
- [x] `SteamNetworkingCore.Close` closes accepted connections before `CloseListenSocket`
- [x] `SteamPeerRegistry` late-registration guard (shutting-down flag)
- [x] `CallbackDispatcher.CancelAll` no longer double-invokes raw handlers with `IntPtr.Zero`
- [x] `CallbackDispatcher.Tick` reentrancy guard — exits cleanly if `Dispose` called from a handler
- [ ] End-to-end loopback: two peers connect via live Steam P2P — manual gate

### Phase 3: Packaging & CI

- [ ] GitHub Actions: build, test, coverage on push
- [ ] NuGet package (`Manifold.Core` standalone)
- [ ] Godot Asset Library submission (`Manifold.Godot`)
- [ ] SDK update workflow (re-run ManifoldGen, validate sizes, publish)

---

## Contributing

Contributions are very welcome! Whether it's a bug report, a missing API, a question about the architecture, or a pull request, feel free to open an issue and start a conversation. The project is still in active development, so if you're planning something larger it's worth reaching out first so we can figure out the best approach together.

---

## License

MIT — Copyright © Matthew Makary
