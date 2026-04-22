# Manifold

A C# Steamworks SDK library for Godot 4 that talks directly to Steam with no man in the middle!

```csharp
var peer = new SteamMultiplayerPeer();
await peer.HostAsync(ELobbyType.FriendsOnly, maxMembers: 4);
Multiplayer.MultiplayerPeer = peer;
// @rpc just works from here.
```

[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE) [![.NET 8](https://img.shields.io/badge/.NET-8-purple)](https://dotnet.microsoft.com) [![Steamworks SDK](https://img.shields.io/badge/Steamworks%20SDK-1.64-informational)](https://partner.steamgames.com) [![Tests](https://img.shields.io/badge/tests-80%20passing-brightgreen)]() [![Status](https://img.shields.io/badge/status-WIP-orange)]()

---

## Why Manifold

Most C# Steam integrations stack abstractions on top of abstractions — Steamworks.NET wraps the SDK in a C++ flat API layer, and the GodotSteam C# bindings wrap GodotSteam's GDExtension on top of that. Manifold goes straight to the native SDK via P/Invoke, which means one less layer to debug and no GDExtension binary to ship or update. The entire P/Invoke surface is auto-generated from the official `steam_api.json` file that ships with the SDK, so keeping up with SDK updates is a code-gen run rather than manual porting. Call results use real `async`/`await` with cancellation and timeout support instead of callback spaghetti. Struct packing is validated against the real SDK headers at build time, so layout bugs get caught before they corrupt your data in production.

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
- [Steamworks SDK 1.64](https://partner.steamgames.com/doc/sdk) — you supply the native binaries, Manifold supplies the bindings

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

Drop the Steamworks SDK native libraries (`steam_api64.dll` / `libsteam_api.so`) into your project's output directory. A NuGet release and proper setup guide will come in Phase 3.

---

## Roadmap

### Phase 1 — Foundation ✅

- [x] Source generator (ManifoldGen) — reads `steam_api.json`, emits full P/Invoke layer
- [x] `SteamLifecycle` — init/shutdown, background callback pump, fatal error handling
- [x] `CallbackDispatcher` — type-safe subscriptions with disposable tokens
- [x] `CallResultAwaiter<T>` — async Steam call results with cancellation and timeout
- [x] Native struct size validator — catches packing bugs against real SDK headers at build time
- [x] 80 contract tests

### Phase 2 — Godot Integration *(in progress)*

- [ ] `SteamManager` autoload (init, shutdown, per-frame pump hook)
- [ ] `SteamMultiplayerPeer` implementing Godot's `MultiplayerPeer`
- [ ] Lobby API (create, join, leave, metadata, member list)
- [ ] `ISteamNetworkingSockets` send/receive pipeline
- [ ] Godot signal wrappers for common callbacks
- [ ] `steam_appid.txt` handling and export plugin

### Phase 3 — Packaging & CI

- [ ] GitHub Actions — build, test, coverage on push
- [ ] NuGet package (`Manifold.Core` standalone)
- [ ] Godot Asset Library submission (`Manifold.Godot`)
- [ ] SDK update workflow (re-run ManifoldGen, validate sizes, publish)

---

## Contributing

PRs are welcome. If you find a bug, hit a missing API, or have a question about the architecture, open an issue. The codebase is still moving fast during Phase 2 so it's worth checking open issues before starting large changes — no point duplicating work.

---

## License

MIT — Copyright © Matthew Makary
