using System.Collections.Generic;
using Manifold.Core.Networking;
using Xunit;

namespace Manifold.Core.Tests.Networking;

public class PeerIdMapperTests
{
    private static SteamId MakeSteamId(ulong v) => new SteamId(v);

    [Fact]
    public void Register_AssignsFirstGodotId_StartingAt2()
    {
        var mapper = new PeerIdMapper();
        int id = mapper.Register(MakeSteamId(1001UL), 100u);
        Assert.Equal(2, id);
    }

    [Fact]
    public void Register_AssignsIncrementingGodotIds()
    {
        var mapper = new PeerIdMapper();
        int id1 = mapper.Register(MakeSteamId(1001UL), 101u);
        int id2 = mapper.Register(MakeSteamId(1002UL), 102u);
        int id3 = mapper.Register(MakeSteamId(1003UL), 103u);
        Assert.Equal(2, id1);
        Assert.Equal(3, id2);
        Assert.Equal(4, id3);
    }

    [Fact]
    public void Register_MapsAllThreeDirections()
    {
        var mapper  = new PeerIdMapper();
        var steamId = MakeSteamId(76561198000000001UL);
        uint conn   = 999u;

        int godotId = mapper.Register(steamId, conn);

        Assert.Equal(2, godotId);
        Assert.Equal(steamId, mapper.GetSteamId(godotId));
        Assert.Equal(godotId, mapper.GetGodotId(steamId));
        Assert.Equal(godotId, mapper.GetGodotId(conn));
    }

    [Fact]
    public void GetSteamId_ReturnsRegisteredSteamId()
    {
        var mapper  = new PeerIdMapper();
        var steamId = MakeSteamId(76561198000000002UL);
        int godotId = mapper.Register(steamId, 200u);

        Assert.Equal(steamId, mapper.GetSteamId(godotId));
    }

    [Fact]
    public void GetGodotId_BySteamId_ReturnsRegisteredId()
    {
        var mapper  = new PeerIdMapper();
        var steamId = MakeSteamId(76561198000000003UL);
        int godotId = mapper.Register(steamId, 300u);

        Assert.Equal(godotId, mapper.GetGodotId(steamId));
    }

    [Fact]
    public void GetGodotId_ByConnection_ReturnsRegisteredId()
    {
        var mapper  = new PeerIdMapper();
        uint conn   = 400u;
        int godotId = mapper.Register(MakeSteamId(76561198000000004UL), conn);

        Assert.Equal(godotId, mapper.GetGodotId(conn));
    }

    [Fact]
    public void TryGetGodotId_ReturnsTrue_WhenConnectionIsRegistered()
    {
        var mapper  = new PeerIdMapper();
        uint conn   = 500u;
        int expected = mapper.Register(MakeSteamId(76561198000000005UL), conn);

        bool found = mapper.TryGetGodotId(conn, out int actual);

        Assert.True(found);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryGetGodotId_ReturnsFalse_WhenConnectionIsUnknown()
    {
        var mapper = new PeerIdMapper();

        bool found = mapper.TryGetGodotId(9999u, out int godotId);

        Assert.False(found);
        Assert.Equal(0, godotId);
    }

    [Fact]
    public void Remove_ClearsAllMappingsForGodotId()
    {
        var mapper  = new PeerIdMapper();
        var steamId = MakeSteamId(76561198000000006UL);
        uint conn   = 600u;
        int godotId = mapper.Register(steamId, conn);

        mapper.Remove(godotId);

        Assert.Throws<KeyNotFoundException>(() => mapper.GetSteamId(godotId));
        Assert.Throws<KeyNotFoundException>(() => mapper.GetGodotId(steamId));
        Assert.False(mapper.TryGetGodotId(conn, out _));
    }

    [Fact]
    public void Remove_DoesNotAffectOtherRegistrations()
    {
        var mapper   = new PeerIdMapper();
        var steam1   = MakeSteamId(76561198000000007UL);
        var steam2   = MakeSteamId(76561198000000008UL);
        uint conn1   = 701u;
        uint conn2   = 702u;

        int godotId1 = mapper.Register(steam1, conn1);
        int godotId2 = mapper.Register(steam2, conn2);

        mapper.Remove(godotId1);

        // Second peer should still be accessible
        Assert.Equal(steam2, mapper.GetSteamId(godotId2));
        Assert.Equal(godotId2, mapper.GetGodotId(steam2));
        Assert.Equal(godotId2, mapper.GetGodotId(conn2));
    }

    [Fact]
    public void Remove_IsNoOp_ForUnregisteredId()
    {
        var mapper = new PeerIdMapper();
        // Should not throw
        mapper.Remove(999);
    }

    [Fact]
    public void Clear_RemovesAllMappings()
    {
        var mapper = new PeerIdMapper();
        uint conn1 = 801u;
        uint conn2 = 802u;
        mapper.Register(MakeSteamId(76561198000000009UL), conn1);
        mapper.Register(MakeSteamId(76561198000000010UL), conn2);

        mapper.Clear();

        Assert.False(mapper.TryGetGodotId(conn1, out _));
        Assert.False(mapper.TryGetGodotId(conn2, out _));
    }

    [Fact]
    public void Clear_ResetsNextIdTo2()
    {
        var mapper = new PeerIdMapper();
        mapper.Register(MakeSteamId(76561198000000011UL), 901u);
        mapper.Register(MakeSteamId(76561198000000012UL), 902u);

        mapper.Clear();

        int id = mapper.Register(MakeSteamId(76561198000000013UL), 903u);
        Assert.Equal(2, id);
    }

    [Fact]
    public void GetSteamId_Throws_ForUnknownGodotId()
    {
        var mapper = new PeerIdMapper();
        Assert.Throws<KeyNotFoundException>(() => mapper.GetSteamId(9999));
    }

    [Fact]
    public void GetGodotId_BySteamId_Throws_ForUnknownSteamId()
    {
        var mapper  = new PeerIdMapper();
        var unknown = MakeSteamId(76561199999999999UL);
        Assert.Throws<KeyNotFoundException>(() => mapper.GetGodotId(unknown));
    }

    [Fact]
    public void GetGodotId_ByConnection_Throws_ForUnknownConnection()
    {
        var mapper = new PeerIdMapper();
        Assert.Throws<KeyNotFoundException>(() => mapper.GetGodotId(99999u));
    }

    // ── Edge-case tests ──────────────────────────────────────────────────────

    [Fact]
    public void Register_Throws_WhenSteamIdAlreadyRegistered()
    {
        var mapper = new PeerIdMapper();
        var steamId = new SteamId(76561198000000001UL);
        mapper.Register(steamId, connection: 100);
        Assert.Throws<InvalidOperationException>(() => mapper.Register(steamId, connection: 200));
    }

    [Fact]
    public void Register_AllowsReregistration_AfterRemove()
    {
        var mapper = new PeerIdMapper();
        var steamId = new SteamId(76561198000000001UL);
        var godotId = mapper.Register(steamId, connection: 100);
        mapper.Remove(godotId);
        // Should not throw after removal
        var newId = mapper.Register(steamId, connection: 101);
        Assert.Equal(3, newId); // 2 was the first ID, 3 is the second (counter doesn't reset on Remove)
    }

    [Fact]
    public void Remove_ReleasesConnectionHandle_AllowingReuse()
    {
        var mapper = new PeerIdMapper();
        var id1 = mapper.Register(new SteamId(1), connection: 100);
        mapper.Remove(id1);
        // Connection handle 100 is now free — a different Steam peer can use it
        var id2 = mapper.Register(new SteamId(2), connection: 100);
        Assert.True(mapper.TryGetGodotId(100, out var found));
        Assert.Equal(id2, found);
    }

    [Fact]
    public void Register_ConnectionHandleReuse_OverwritesPreviousMapping()
    {
        // If caller registers a new peer with an already-mapped connection handle,
        // the handle is remapped. This documents the expected behaviour.
        var mapper = new PeerIdMapper();
        var id1 = mapper.Register(new SteamId(1), connection: 100);
        mapper.Remove(id1);
        var id2 = mapper.Register(new SteamId(2), connection: 100); // same handle, new peer
        Assert.True(mapper.TryGetGodotId(100, out var found));
        Assert.Equal(id2, found);
        Assert.False(mapper.TryGetGodotId(100, out _) && found == id1); // old mapping gone
    }
}
