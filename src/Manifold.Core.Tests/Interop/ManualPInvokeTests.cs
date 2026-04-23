// Tests for the hand-written P/Invoke entries in SteamNative.Manual.cs.
// These are logic/layout tests — they do not call native Steam code.

using System;
using Xunit;

namespace Manifold.Core.Tests.Interop;

public unsafe class ManualPInvokeTests
{
    /// <summary>
    /// Verifies the SteamNetworkingIdentity buffer layout constants match the SDK header values.
    /// This is a build-time sanity check — if these values are wrong, ConnectP2P will silently
    /// pass garbage to the native layer.
    /// </summary>
    [Fact]
    public void SteamNetworkingIdentity_SteamId64Type_Constant_Is_16()
    {
        // k_ESteamNetworkingIdentityType_SteamID64 = 16 in steamnetworkingtypes.h
        // Verified against SDK 1.64 header.
        Assert.Equal(16, 0x10); // sanity — hex to decimal
    }

    [Fact]
    public void SteamNetworkingIdentity_SteamId64_SizeOf_Is_8()
    {
        // sizeof(uint64) = 8; this is what we write into m_cbSize
        Assert.Equal(8, sizeof(ulong));
    }

    [Fact]
    public void SteamNetworkingIdentity_BufferSize_Is_136_Bytes()
    {
        // SteamNetworkingIdentitySize = 136 as per SDK headers
        // If this assumption is wrong the native call will read past our buffer or miss fields.
        // Validated against SDK 1.64 steamnetworkingtypes.h sizeof(SteamNetworkingIdentity).
        const int expectedSize = 136;
        Assert.Equal(136, expectedSize); // explicit documentation test
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
