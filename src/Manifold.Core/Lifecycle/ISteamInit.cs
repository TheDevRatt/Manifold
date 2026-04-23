// Manifold — ISteamInit
// Abstraction over SteamAPI lifecycle calls.
// Allows SteamLifecycle to be tested without touching the real Steam DLL.

namespace Manifold.Core.Lifecycle;

/// <summary>
/// Wraps the low-level SteamAPI lifecycle calls and accessors.
/// Production: <see cref="LiveSteamInit"/>.
/// Tests: <see cref="FakeSteamInit"/>.
/// </summary>
public interface ISteamInit
{
    /// <summary>
    /// Initialises the Steam API.
    /// </summary>
    /// <returns><c>true</c> on success; <c>false</c> if Steam is not running or the app ID is wrong.</returns>
    bool Init();

    /// <summary>Shuts down the Steam API and releases resources.</summary>
    void Shutdown();

    /// <summary>
    /// Opts in to the manual-dispatch callback model.
    /// Must be called once after <see cref="Init"/> when <c>ManualDispatch</c> is enabled.
    /// No-op in test doubles.
    /// </summary>
    void ManualDispatchInit();

    /// <summary>
    /// Pumps the Steam callback queue. Called on the game thread once per frame.
    /// Production: calls <c>SteamAPI_ManualDispatch_RunFrame</c> (or legacy <c>SteamAPI_RunCallbacks</c>).
    /// </summary>
    void RunCallbacks();

    /// <summary>
    /// Returns the <c>HSteamPipe</c> handle required for manual-dispatch frame pumping.
    /// Returns 0 in test doubles.
    /// </summary>
    uint GetHSteamPipe();

    /// <summary>
    /// Returns the local user's Steam 64-bit ID.
    /// Returns 0 in test doubles.
    /// </summary>
    ulong GetLocalSteamId();

    /// <summary>
    /// Returns the AppID of the currently running application.
    /// Returns 0 in test doubles.
    /// </summary>
    uint GetAppId();
}
