// Manifold — LiveSteamInit
// Production ISteamInit that calls the actual SteamAPI P/Invoke methods.

using System;
using Manifold.Core.Interop;

namespace Manifold.Core.Lifecycle;

/// <summary>
/// Production implementation of <see cref="ISteamInit"/>: delegates to
/// the generated SteamNative P/Invoke layer.
/// </summary>
public sealed class LiveSteamInit : ISteamInit
{
    /// <inheritdoc/>
    public bool Init() => SteamNative.SteamAPI_Init();

    /// <inheritdoc/>
    public void Shutdown() => SteamNative.SteamAPI_Shutdown();

    /// <inheritdoc/>
    public void ManualDispatchInit() => SteamNative.SteamAPI_ManualDispatch_Init();

    /// <inheritdoc/>
    /// <remarks>
    /// Uses the legacy <c>SteamAPI_RunCallbacks</c> path. When ManualDispatch is
    /// enabled <see cref="SteamLifecycle"/> calls <c>SteamAPI_ManualDispatch_RunFrame</c>
    /// directly via <see cref="GetHSteamPipe"/>; this method exists for completeness.
    /// </remarks>
    public void RunCallbacks() => SteamNative.SteamAPI_RunCallbacks();

    /// <inheritdoc/>
    public uint GetHSteamPipe()
        => SteamNative.SteamAPI_GetHSteamPipe();

    /// <inheritdoc/>
    public ulong GetLocalSteamId()
    {
        var userPtr = SteamNative.SteamAPI_SteamUser_v023();
        if (userPtr == IntPtr.Zero) return 0;
        return SteamNative.User_GetSteamID(userPtr);
    }

    /// <inheritdoc/>
    public uint GetAppId()
    {
        var utilsPtr = SteamNative.SteamAPI_SteamUtils_v010();
        if (utilsPtr == IntPtr.Zero) return 0;
        return SteamNative.Utils_GetAppID(utilsPtr);
    }
}
