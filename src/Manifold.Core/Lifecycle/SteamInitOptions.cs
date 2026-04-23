// Manifold — SteamInitOptions
// Configuration passed to SteamLifecycle.Initialize().

namespace Manifold.Core.Lifecycle;

/// <summary>
/// Configuration options for <see cref="SteamLifecycle.Initialize"/>.
/// </summary>
public sealed record SteamInitOptions
{
    /// <summary>
    /// Steam AppID to use. 0 means read from steam_appid.txt on disk.
    /// </summary>
    public uint AppId { get; init; } = 0;

    /// <summary>
    /// When <c>true</c>, relaunch through Steam if the process was not started via Steam.
    /// </summary>
    public bool AllowRestart { get; init; } = false;

    /// <summary>
    /// When <c>true</c>, call <c>SteamAPI_ManualDispatch_Init</c> after initialisation
    /// and use the manual-dispatch pump model instead of the legacy <c>SteamAPI_RunCallbacks</c>.
    /// </summary>
    public bool ManualDispatch { get; init; } = true;

    /// <summary>
    /// How long a pending <c>CallResultAwaiter</c> may wait before being cancelled with
    /// <see cref="SteamCallResultTimeoutException"/>.
    /// </summary>
    public TimeSpan CallResultTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
