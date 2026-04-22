# Manifold

**A Godot-first C# Steamworks SDK library.**

Manifold wraps the Steamworks SDK directly for Godot 4 C# developers — no GodotSteam dependency, no Steamworks.NET middleman. Just clean, idiomatic C# that plugs into Godot's multiplayer system.

```csharp
var peer = new SteamMultiplayerPeer();
await peer.HostWithLobbyAsync(ELobbyType.FriendsOnly, maxMembers: 4);
Multiplayer.MultiplayerPeer = peer;
// @rpc just works.
```

## Status

🚧 **Early development — Phase 1 (Foundation)**

## Packages

| Package | Description |
|---|---|
| `Manifold.Core` | Engine-agnostic Steam API wrappers, no Godot dependency |
| `Manifold.Godot` | Godot 4 integration — `SteamMultiplayerPeer`, `SteamManager`, signals |

## Requirements

- Godot 4.3+ (.NET)
- .NET 8
- Steamworks SDK 1.64 (native binaries bundled in NuGet package)

## Design

See [MASTER_DESIGN.md](MASTER_DESIGN.md) for full architecture documentation.

## License

MIT
