// Hand-written P/Invoke declarations skipped by ManifoldGen.
// These require unsafe layout knowledge or complex parameter types.
// DO NOT run ManifoldGen on this file — it is not auto-generated.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Manifold.Core.Interop;

internal static partial class SteamNative
{
    // ── SteamAPI_GetHSteamPipe — manually implemented (absent from steam_api.json) ──

    /// <summary>
    /// Returns the global <c>HSteamPipe</c> for the current Steam user session.
    /// Required for <c>SteamAPI_ManualDispatch_*</c> calls.
    /// Defined in SDK <c>steam_api_internal.h</c>; absent from <c>steam_api.json</c> so not auto-generated.
    /// </summary>
    [DllImport(LibName, EntryPoint = "SteamAPI_GetHSteamPipe",
               CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint SteamAPI_GetHSteamPipe();

    // ── ConnectP2P — manually implemented (ManifoldGen skipped: SteamNetworkingIdentity& param) ──

    // Identity type constant for the SteamID64 variant.
    // Sourced from Steamworks SDK 1.64 steamnetworkingtypes.h: k_ESteamNetworkingIdentityType_SteamID64 = 16.
    // ⚠ Verify on every SDK upgrade.
    internal const int k_ESteamNetworkingIdentityType_SteamID64 = 16;

    // Total size of SteamNetworkingIdentity in bytes.
    // Sourced from Steamworks SDK 1.64 steamnetworkingtypes.h: sizeof(SteamNetworkingIdentity) = 136.
    // ⚠ Verify on every SDK upgrade — Valve has changed struct layouts before without announcement.
    internal const int SteamNetworkingIdentitySize = 136;

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

        // pOptions is a SteamNetworkingConfigValue_t* array for per-connection options.
        // Passing IntPtr.Zero (nOptions=0) uses Steam's defaults — sufficient for standard P2P games.
        return ConnectP2P_Native(self, identity, nRemoteVirtualPort, nOptions, IntPtr.Zero);
    }

    // DllImport used instead of LibraryImport: the byte* pIdentity parameter is a raw unsafe pointer
    // with no marshalling transform needed. LibraryImport's source generator does not support
    // unsafe pointer parameters without additional generated wrappers, making DllImport the correct
    // and simpler choice for this hand-written unsafe interop entry. See MASTER_DESIGN §6 (§1).
    /// <summary>
    /// Raw P/Invoke entry for <c>SteamAPI_ISteamNetworkingSockets_ConnectP2P</c>.
    /// <c>pIdentity</c> must point to a populated <c>SteamNetworkingIdentity</c> buffer (136 bytes).
    /// <c>pOptions</c> is a pointer to a <c>SteamNetworkingConfigValue_t</c> array; pass <see cref="IntPtr.Zero"/> for defaults.
    /// Returns 0 (<c>k_HSteamNetConnection_Invalid</c>) on failure.
    /// </summary>
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

/// <summary>
/// Steam networking message struct. Layout verified against Steamworks SDK 1.64 steamnetworkingtypes.h.
/// Pack=1 used for layout correctness; field offsets match SDK struct layout.
/// Obtained from ReceiveMessagesOnConnection / ReceiveMessagesOnPollGroup as a raw pointer.
/// Must call <see cref="Release"/> after consuming m_pData to free the Steam-owned buffer.
/// (Steamworks SDK 1.64 steamnetworkingtypes.h)
/// </summary>
[System.Runtime.InteropServices.StructLayout(
    System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
internal unsafe struct SteamNetworkingMessage_t
{
    /// <summary>Pointer to the message payload data. Valid only until Release() is called.</summary>
    internal System.IntPtr m_pData;         // pointer to message payload
    /// <summary>Size of the payload data in bytes.</summary>
    internal int           m_cbSize;        // payload size in bytes
    /// <summary>HSteamNetConnection handle — identifies the sender.</summary>
    internal uint          m_conn;          // HSteamNetConnection — sender
    // SteamNetworkingIdentity m_identitySender — 136 bytes, skip via padding
    private fixed byte     _identityPad[136];
    /// <summary>User-defined connection data (set via SetConnectionUserData).</summary>
    internal long          m_nConnUserData;
    /// <summary>Timestamp when the message was received (microseconds).</summary>
    internal long          m_usecTimeReceived;
    /// <summary>Message sequence number.</summary>
    internal long          m_nMessageNumber;
    /// <summary>Internal data-free callback. Do not call directly; use Release().</summary>
    internal System.IntPtr m_pfnFreeData;
    /// <summary>Message release callback. Always call Release() to free the message.</summary>
    internal System.IntPtr m_pfnRelease;    // call this to free the message
    /// <summary>The channel this message was sent on.</summary>
    internal int           m_nChannel;
    /// <summary>Lane index (for multi-lane connections).</summary>
    internal int           m_idxLane;
    /// <summary>Reserved padding.</summary>
    internal int           m_pad;

    /// <summary>
    /// Releases this message and its data buffer back to the Steam library.
    /// Must be called exactly once after the message data has been consumed.
    /// </summary>
    internal unsafe void Release()
    {
        if (m_pfnRelease != System.IntPtr.Zero)
        {
            var fn = (delegate* unmanaged[Cdecl]<System.IntPtr, void>)m_pfnRelease;
            fixed (SteamNetworkingMessage_t* self = &this)
                fn((System.IntPtr)self);
        }
    }
}
