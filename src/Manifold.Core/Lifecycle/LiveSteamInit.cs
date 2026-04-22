// Manifold — LiveSteamInit
// Production ISteamInit that calls the actual SteamAPI P/Invoke methods.

using Manifold.Core.Interop;

namespace Manifold.Core.Lifecycle;

/// <summary>
/// Production implementation of <see cref="ISteamInit"/>: delegates to
/// the generated SteamNative P/Invoke layer.
/// </summary>
public sealed class LiveSteamInit : ISteamInit
{
    /// <inheritdoc/>
    public void Init()
    {
        if (!SteamNative.SteamAPI_Init())
            throw new SteamInitException("SteamAPI_Init() returned false. " +
                "Ensure Steam is running and steam_appid.txt is present.");
    }

    /// <inheritdoc/>
    public void RunCallbacks() => SteamNative.SteamAPI_RunCallbacks();

    /// <inheritdoc/>
    public void Shutdown() => SteamNative.SteamAPI_Shutdown();
}
