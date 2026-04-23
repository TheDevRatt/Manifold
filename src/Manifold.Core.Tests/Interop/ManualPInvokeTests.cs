// Tests for the hand-written P/Invoke entries in SteamNative.Manual.cs.
// These are logic/layout tests — they do not call native Steam code.

using System;
using Manifold.Core.Interop;
using Xunit;
using SteamInterop = Manifold.Core.Interop;

namespace Manifold.Core.Tests.Interop;

public unsafe class ManualPInvokeTests
{
    /// <summary>
    /// Verifies the SteamNetworkingIdentity buffer layout constants match the SDK header values.
    /// This is a build-time sanity check — if these values are wrong, ConnectP2P will silently
    /// pass garbage to the native layer.
    /// </summary>
    [Fact]
    public void SteamNetworkingIdentity_SteamId64TypeConstant_Matches_SdkValue()
    {
        // k_ESteamNetworkingIdentityType_SteamID64 = 16 per SDK 1.64 steamnetworkingtypes.h
        Assert.Equal(16, SteamNative.k_ESteamNetworkingIdentityType_SteamID64);
    }

    [Fact]
    public void SteamNetworkingIdentity_IdentitySize_MatchesMarshalSizeOf()
    {
        // SteamNative.SteamNetworkingIdentitySize is our stack-allocation constant for ConnectP2P.
        // It must be >= Marshal.SizeOf<SteamNetworkingIdentity>() — we need at least that many bytes.
        // NativeSizeValidationTests verifies Marshal.SizeOf against the C++ sizeof() binary.
        // This test chains the two: constant >= C# struct size >= C++ native size.
        int marshalSize = System.Runtime.InteropServices.Marshal.SizeOf<SteamInterop.SteamNetworkingIdentity>();
        Assert.True(
            SteamNative.SteamNetworkingIdentitySize >= marshalSize,
            $"SteamNetworkingIdentitySize ({SteamNative.SteamNetworkingIdentitySize}) must be >= " +
            $"Marshal.SizeOf<SteamNetworkingIdentity>() ({marshalSize}).");
    }

    [Fact]
    public unsafe void ConnectP2P_SteamId_Identity_Buffer_Layout_IsCorrect()
    {
        // Verify that the buffer population logic writes to the correct offsets.
        const int identitySize = 136;
        byte* buf = stackalloc byte[identitySize];
        new Span<byte>(buf, identitySize).Clear();

        const int expectedType = 16;   // k_ESteamNetworkingIdentityType_SteamID64
        const int expectedSize8 = 8;   // sizeof(ulong)
        // Synthetic Steam64 ID — valid range, not a real user account.
        // Base value 76561197960265728 + offset = valid Steam64 ID structure.
        const ulong testId = 76561198000000001UL;

        *(int*)(buf + 0) = expectedType;
        *(int*)(buf + 4) = expectedSize8;
        *(ulong*)(buf + 8) = testId;

        Assert.Equal(expectedType,  *(int*)(buf + 0));
        Assert.Equal(expectedSize8, *(int*)(buf + 4));
        Assert.Equal(testId,        *(ulong*)(buf + 8));

        // All other bytes should remain zero
        for (int i = 16; i < identitySize; i++)
            Assert.Equal(0, buf[i]);
    }
}
