// Manifold — ISteamInit
// Abstraction over SteamAPI_Init / SteamAPI_Shutdown / SteamAPI_RunCallbacks.
// Allows SteamLifecycle to be tested without touching the real Steam DLL.

namespace Manifold.Core.Lifecycle;

/// <summary>
/// Wraps the three low-level SteamAPI lifecycle calls.
/// Production: <see cref="LiveSteamInit"/>.
/// Tests: <see cref="FakeSteamInit"/>.
/// </summary>
public interface ISteamInit
{
    /// <summary>
    /// Initialises the Steam API.
    /// </summary>
    /// <exception cref="SteamInitException">Thrown on failure.</exception>
    void Init();

    /// <summary>Pumps the Steam callback queue. Called on the pump thread.</summary>
    void RunCallbacks();

    /// <summary>Shuts down the Steam API and releases resources.</summary>
    void Shutdown();
}
