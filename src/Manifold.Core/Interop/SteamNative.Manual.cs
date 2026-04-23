// Hand-written P/Invoke declarations skipped by ManifoldGen.
// These require unsafe layout knowledge or complex parameter types.
// DO NOT run ManifoldGen on this file — it is not auto-generated.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Manifold.Core.Interop;

internal static partial class SteamNative
{
    // ── ConnectP2P — manually implemented (ManifoldGen skipped: SteamNetworkingIdentity& param) ──

    // Identity type constant for the SteamID64 variant
    private const int k_ESteamNetworkingIdentityType_SteamID64 = 16;

    // SteamNetworkingIdentity total struct size (136 bytes, per SDK headers)
    private const int SteamNetworkingIdentitySize = 136;

    /// <summary>
    /// Initiates a P2P connection to a remote peer identified by their Steam64 ID.
    /// Wraps <c>SteamAPI_ISteamNetworkingSockets_ConnectP2P</c> which was skipped by ManifoldGen
    /// because <c>SteamNetworkingIdentity</c> is a C++ union that cannot be auto-generated safely.
    /// Only the SteamID64 identity variant is supported — which covers all standard P2P game use cases.
    /// </summary>
    /// <param name="self">ISteamNetworkingSockets accessor pointer.</param>
    /// <param name="remoteSteamId64">The remote peer's Steam64 ID.</param>
    /// <param name="nRemoteVirtualPort">Virtual port to connect on. Must match the host's listen socket port.</param>
    /// <param name="nOptions">Number of configuration options (pass 0).</param>
    /// <returns>
    /// A <c>HSteamNetConnection</c> handle (as <see cref="uint"/>), or
    /// <c>k_HSteamNetConnection_Invalid</c> (0) on failure.
    /// </returns>
    internal static unsafe uint NetworkingSockets_ConnectP2P(
        IntPtr self,
        ulong remoteSteamId64,
        int nRemoteVirtualPort,
        int nOptions = 0)
    {
        // Stack-allocate a zeroed SteamNetworkingIdentity buffer
        byte* identity = stackalloc byte[SteamNetworkingIdentitySize];
        new Span<byte>(identity, SteamNetworkingIdentitySize).Clear();

        // Populate the SteamID64 variant:
        //   offset 0: m_eType (int32) = k_ESteamNetworkingIdentityType_SteamID64
        //   offset 4: m_cbSize (int32) = sizeof(uint64) = 8
        //   offset 8: m_steamID64 (uint64) = the remote Steam64 ID
        *(int*)(identity + 0) = k_ESteamNetworkingIdentityType_SteamID64;
        *(int*)(identity + 4) = sizeof(ulong);  // 8
        *(ulong*)(identity + 8) = remoteSteamId64;

        return ConnectP2P_Native(self, identity, nRemoteVirtualPort, nOptions, IntPtr.Zero);
    }

    [DllImport(LibName,
               EntryPoint = "SteamAPI_ISteamNetworkingSockets_ConnectP2P",
               CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe uint ConnectP2P_Native(
        IntPtr self,
        byte* pIdentity,
        int nRemoteVirtualPort,
        int nOptions,
        IntPtr pOptions);
}
