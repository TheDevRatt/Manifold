// Manifold — Native struct size validation tests
//
// Builds the SizeValidator C++ binary (if needed), runs it, and asserts that
// every generated interop struct has the same size in C# (Marshal.SizeOf<T>())
// as in the native Steam SDK headers.
//
// This catches struct packing mismatches before they cause silent data corruption.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using SteamInterop = Manifold.Core.Interop;

namespace Manifold.Core.Tests.Interop;

public sealed class NativeSizeValidationTests
{
    private readonly ITestOutputHelper _out;

    public NativeSizeValidationTests(ITestOutputHelper output) => _out = output;

    // ── Struct size table ─────────────────────────────────────────────────────
    // Maps C# type → expected name used in the C++ size_validator output.
    // Only structs that exist in both the generated C# and SDK 1.64 headers.

    private static readonly (Type Type, string NativeName)[] StructTable = new[]
    {
        // Callback structs
        (typeof(SteamInterop.SteamServersConnected_t),          "SteamServersConnected_t"),
        (typeof(SteamInterop.SteamServerConnectFailure_t),      "SteamServerConnectFailure_t"),
        (typeof(SteamInterop.SteamServersDisconnected_t),       "SteamServersDisconnected_t"),
        (typeof(SteamInterop.ClientGameServerDeny_t),           "ClientGameServerDeny_t"),
        (typeof(SteamInterop.IPCFailure_t),                     "IPCFailure_t"),
        (typeof(SteamInterop.LicensesUpdated_t),                "LicensesUpdated_t"),
        (typeof(SteamInterop.ValidateAuthTicketResponse_t),     "ValidateAuthTicketResponse_t"),
        (typeof(SteamInterop.MicroTxnAuthorizationResponse_t),  "MicroTxnAuthorizationResponse_t"),
        (typeof(SteamInterop.EncryptedAppTicketResponse_t),     "EncryptedAppTicketResponse_t"),
        (typeof(SteamInterop.GetAuthSessionTicketResponse_t),   "GetAuthSessionTicketResponse_t"),
        (typeof(SteamInterop.PersonaStateChange_t),             "PersonaStateChange_t"),
        (typeof(SteamInterop.GameOverlayActivated_t),           "GameOverlayActivated_t"),
        (typeof(SteamInterop.GameLobbyJoinRequested_t),         "GameLobbyJoinRequested_t"),
        (typeof(SteamInterop.AvatarImageLoaded_t),              "AvatarImageLoaded_t"),
        (typeof(SteamInterop.FriendRichPresenceUpdate_t),       "FriendRichPresenceUpdate_t"),
        (typeof(SteamInterop.GameConnectedChatLeave_t),         "GameConnectedChatLeave_t"),
        (typeof(SteamInterop.LobbyDataUpdate_t),                "LobbyDataUpdate_t"),
        (typeof(SteamInterop.LobbyChatUpdate_t),                "LobbyChatUpdate_t"),
        (typeof(SteamInterop.LobbyMatchList_t),                 "LobbyMatchList_t"),
        (typeof(SteamInterop.LobbyKicked_t),                    "LobbyKicked_t"),
        (typeof(SteamInterop.LobbyCreated_t),                   "LobbyCreated_t"),
        (typeof(SteamInterop.SteamAPICallCompleted_t),          "SteamAPICallCompleted_t"),
        (typeof(SteamInterop.SteamShutdown_t),                  "SteamShutdown_t"),
        (typeof(SteamInterop.UserStatsReceived_t),              "UserStatsReceived_t"),
        (typeof(SteamInterop.UserStatsStored_t),                "UserStatsStored_t"),
        (typeof(SteamInterop.UserAchievementStored_t),          "UserAchievementStored_t"),
        (typeof(SteamInterop.LeaderboardFindResult_t),          "LeaderboardFindResult_t"),
        (typeof(SteamInterop.LeaderboardScoreUploaded_t),       "LeaderboardScoreUploaded_t"),
        (typeof(SteamInterop.NumberOfCurrentPlayers_t),         "NumberOfCurrentPlayers_t"),
        (typeof(SteamInterop.UserStatsUnloaded_t),              "UserStatsUnloaded_t"),
        (typeof(SteamInterop.GlobalAchievementPercentagesReady_t), "GlobalAchievementPercentagesReady_t"),
        (typeof(SteamInterop.GlobalStatsReceived_t),            "GlobalStatsReceived_t"),
        (typeof(SteamInterop.HTTPRequestCompleted_t),           "HTTPRequestCompleted_t"),
        (typeof(SteamInterop.HTTPRequestHeadersReceived_t),     "HTTPRequestHeadersReceived_t"),
        (typeof(SteamInterop.HTTPRequestDataReceived_t),        "HTTPRequestDataReceived_t"),
        (typeof(SteamInterop.SteamInventoryResultReady_t),      "SteamInventoryResultReady_t"),
        (typeof(SteamInterop.SteamInventoryFullUpdate_t),       "SteamInventoryFullUpdate_t"),
        (typeof(SteamInterop.SteamNetConnectionStatusChangedCallback_t), "SteamNetConnectionStatusChangedCallback_t"),
        (typeof(SteamInterop.SteamNetAuthenticationStatus_t),   "SteamNetAuthenticationStatus_t"),
        (typeof(SteamInterop.SteamRelayNetworkStatus_t),        "SteamRelayNetworkStatus_t"),
        (typeof(SteamInterop.SteamRemotePlaySessionConnected_t),    "SteamRemotePlaySessionConnected_t"),
        (typeof(SteamInterop.SteamRemotePlaySessionDisconnected_t), "SteamRemotePlaySessionDisconnected_t"),
        (typeof(SteamInterop.SteamNetworkingMessagesSessionRequest_t),  "SteamNetworkingMessagesSessionRequest_t"),
        (typeof(SteamInterop.SteamNetworkingMessagesSessionFailed_t),   "SteamNetworkingMessagesSessionFailed_t"),
        (typeof(SteamInterop.SteamInputDeviceConnected_t),      "SteamInputDeviceConnected_t"),
        (typeof(SteamInterop.SteamInputDeviceDisconnected_t),   "SteamInputDeviceDisconnected_t"),
        (typeof(SteamInterop.ItemInstalled_t),                  "ItemInstalled_t"),
        (typeof(SteamInterop.DownloadItemResult_t),             "DownloadItemResult_t"),
        // Plain structs
        (typeof(SteamInterop.P2PSessionState_t),                "P2PSessionState_t"),
        (typeof(SteamInterop.InputAnalogActionData_t),          "InputAnalogActionData_t"),
        (typeof(SteamInterop.InputDigitalActionData_t),         "InputDigitalActionData_t"),
        (typeof(SteamInterop.InputMotionData_t),                "InputMotionData_t"),
        (typeof(SteamInterop.SteamNetworkingIPAddr),            "SteamNetworkingIPAddr"),
        (typeof(SteamInterop.SteamNetworkingIdentity),          "SteamNetworkingIdentity"),
        (typeof(SteamInterop.SteamNetConnectionInfo_t),         "SteamNetConnectionInfo_t"),
        (typeof(SteamInterop.SteamNetConnectionRealTimeStatus_t), "SteamNetConnectionRealTimeStatus_t"),
        (typeof(SteamInterop.SteamNetConnectionRealTimeLaneStatus_t), "SteamNetConnectionRealTimeLaneStatus_t"),
        (typeof(SteamInterop.SteamNetworkPingLocation_t),       "SteamNetworkPingLocation_t"),
    };

    // ── Test ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AllStructSizes_MatchNativeSDK()
    {
        var nativeSizes = RunSizeValidator();

        var mismatches = new List<string>();
        var skipped = new List<string>();

        foreach (var (type, name) in StructTable)
        {
            if (!nativeSizes.TryGetValue(name, out int nativeSize))
            {
                skipped.Add(name);
                _out.WriteLine($"  SKIP  {name} (not in size_validator output)");
                continue;
            }

            int csSize = Marshal.SizeOf(type);
            if (csSize != nativeSize)
            {
                mismatches.Add($"{name}: C#={csSize} native={nativeSize}");
                _out.WriteLine($"  FAIL  {name}: C# Marshal.SizeOf={csSize}  native sizeof={nativeSize}");
            }
            else
            {
                _out.WriteLine($"  OK    {name}: {csSize} bytes");
            }
        }

        if (skipped.Count > 0)
            _out.WriteLine($"\n{skipped.Count} struct(s) skipped (not found in native output).");

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} size mismatch(es) detected:\n" +
            string.Join("\n", mismatches));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, int> RunSizeValidator()
    {
        // Resolve the binary path relative to the repo root
        var repoRoot = FindRepoRoot();
        var binary = Path.Combine(repoRoot, "tools", "SizeValidator", "build", "size_validator");

        if (!File.Exists(binary))
            throw new InvalidOperationException(
                $"size_validator binary not found at: {binary}\n" +
                "Run: cd tools/SizeValidator && cmake -B build && cmake --build build");

        var psi = new System.Diagnostics.ProcessStartInfo(binary)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start size_validator.");

        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"size_validator exited with code {proc.ExitCode}:\n{proc.StandardError.ReadToEnd()}");

        var result = new Dictionary<string, int>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ');
            if (parts.Length == 2 && int.TryParse(parts[1], out int size))
                result[parts[0]] = size;
        }

        return result;
    }

    private static string FindRepoRoot()
    {
        // Walk up from the test assembly until we find Manifold.sln
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Manifold.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate Manifold.sln — cannot find repo root.");
    }
}
