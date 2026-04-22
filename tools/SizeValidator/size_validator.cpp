// SizeValidator — prints "StructName sizeof" lines to stdout, one per struct.
// Build with CMake; the .NET test runner executes this and compares against
// Marshal.SizeOf<T>() for each generated interop struct.
//
// Usage: ./size_validator
// Output format: "SteamServersConnected_t 4\n..."

#include <cstdio>
#include <cstddef>

// Steam SDK headers — packed for the current platform
// VALVE_CALLBACK_PACK_SMALL (Pack=4) on Linux/macOS
// VALVE_CALLBACK_PACK_LARGE (Pack=8) on Windows
#include "steam/steam_api.h"
#include "steam/steamnetworkingtypes.h"

int main(void)
{
    // ── Callback structs (from steam_api.h and friends) ───────────────────────
    printf("SteamServersConnected_t %zu\n",          sizeof(SteamServersConnected_t));
    printf("SteamServerConnectFailure_t %zu\n",      sizeof(SteamServerConnectFailure_t));
    printf("SteamServersDisconnected_t %zu\n",       sizeof(SteamServersDisconnected_t));
    printf("ClientGameServerDeny_t %zu\n",           sizeof(ClientGameServerDeny_t));
    printf("IPCFailure_t %zu\n",                     sizeof(IPCFailure_t));
    printf("LicensesUpdated_t %zu\n",                sizeof(LicensesUpdated_t));
    printf("ValidateAuthTicketResponse_t %zu\n",     sizeof(ValidateAuthTicketResponse_t));
    printf("MicroTxnAuthorizationResponse_t %zu\n",  sizeof(MicroTxnAuthorizationResponse_t));
    printf("EncryptedAppTicketResponse_t %zu\n",     sizeof(EncryptedAppTicketResponse_t));
    printf("GetAuthSessionTicketResponse_t %zu\n",   sizeof(GetAuthSessionTicketResponse_t));
    printf("GameWebCallback_t %zu\n",                sizeof(GameWebCallback_t));
    printf("StoreAuthURLResponse_t %zu\n",           sizeof(StoreAuthURLResponse_t));
    printf("MarketEligibilityResponse_t %zu\n",      sizeof(MarketEligibilityResponse_t));
    printf("DurationControl_t %zu\n",                sizeof(DurationControl_t));
    printf("GetTicketForWebApiResponse_t %zu\n",     sizeof(GetTicketForWebApiResponse_t));
    printf("PersonaStateChange_t %zu\n",             sizeof(PersonaStateChange_t));
    printf("GameOverlayActivated_t %zu\n",           sizeof(GameOverlayActivated_t));
    printf("GameServerChangeRequested_t %zu\n",      sizeof(GameServerChangeRequested_t));
    printf("GameLobbyJoinRequested_t %zu\n",         sizeof(GameLobbyJoinRequested_t));
    printf("AvatarImageLoaded_t %zu\n",              sizeof(AvatarImageLoaded_t));
    printf("ClanOfficerListResponse_t %zu\n",        sizeof(ClanOfficerListResponse_t));
    printf("FriendRichPresenceUpdate_t %zu\n",       sizeof(FriendRichPresenceUpdate_t));
    printf("GameRichPresenceJoinRequested_t %zu\n",  sizeof(GameRichPresenceJoinRequested_t));
    printf("GameConnectedClanChatMsg_t %zu\n",       sizeof(GameConnectedClanChatMsg_t));
    printf("GameConnectedChatJoin_t %zu\n",          sizeof(GameConnectedChatJoin_t));
    printf("GameConnectedChatLeave_t %zu\n",         sizeof(GameConnectedChatLeave_t));
    printf("DownloadClanActivityCountsResult_t %zu\n", sizeof(DownloadClanActivityCountsResult_t));
    printf("JoinClanChatRoomCompletionResult_t %zu\n", sizeof(JoinClanChatRoomCompletionResult_t));
    printf("GameConnectedFriendChatMsg_t %zu\n",     sizeof(GameConnectedFriendChatMsg_t));
    printf("FriendsGetFollowerCount_t %zu\n",        sizeof(FriendsGetFollowerCount_t));
    printf("FriendsIsFollowing_t %zu\n",             sizeof(FriendsIsFollowing_t));
    printf("FriendsEnumerateFollowingList_t %zu\n",  sizeof(FriendsEnumerateFollowingList_t));
    printf("UnreadChatMessagesChanged_t %zu\n",      sizeof(UnreadChatMessagesChanged_t));
    printf("OverlayBrowserProtocolNavigation_t %zu\n", sizeof(OverlayBrowserProtocolNavigation_t));
    printf("EquippedProfileItemsChanged_t %zu\n",    sizeof(EquippedProfileItemsChanged_t));
    printf("EquippedProfileItems_t %zu\n",           sizeof(EquippedProfileItems_t));
    printf("LobbyDataUpdate_t %zu\n",                sizeof(LobbyDataUpdate_t));
    printf("LobbyChatUpdate_t %zu\n",                sizeof(LobbyChatUpdate_t));
    printf("LobbyChatMsg_t %zu\n",                   sizeof(LobbyChatMsg_t));
    printf("LobbyGameCreated_t %zu\n",               sizeof(LobbyGameCreated_t));
    printf("LobbyMatchList_t %zu\n",                 sizeof(LobbyMatchList_t));
    printf("LobbyKicked_t %zu\n",                    sizeof(LobbyKicked_t));
    printf("LobbyCreated_t %zu\n",                   sizeof(LobbyCreated_t));
    printf("FavoritesListAccountsUpdated_t %zu\n",   sizeof(FavoritesListAccountsUpdated_t));
    printf("JoinPartyCallback_t %zu\n",              sizeof(JoinPartyCallback_t));
    printf("CreateBeaconCallback_t %zu\n",           sizeof(CreateBeaconCallback_t));
    printf("ReservationNotificationCallback_t %zu\n", sizeof(ReservationNotificationCallback_t));
    printf("ChangeNumOpenSlotsCallback_t %zu\n",     sizeof(ChangeNumOpenSlotsCallback_t));
    printf("AvailableBeaconLocationsUpdated_t %zu\n", sizeof(AvailableBeaconLocationsUpdated_t));
    printf("ActiveBeaconsUpdated_t %zu\n",           sizeof(ActiveBeaconsUpdated_t));
    printf("RemoteStorageFileReadAsyncComplete_t %zu\n", sizeof(RemoteStorageFileReadAsyncComplete_t));
    printf("RemoteStorageFileWriteAsyncComplete_t %zu\n", sizeof(RemoteStorageFileWriteAsyncComplete_t));
    printf("RemoteStorageFileShareResult_t %zu\n",   sizeof(RemoteStorageFileShareResult_t));
    printf("RemoteStoragePublishFileResult_t %zu\n", sizeof(RemoteStoragePublishFileResult_t));
    printf("RemoteStorageDeletePublishedFileResult_t %zu\n", sizeof(RemoteStorageDeletePublishedFileResult_t));
    printf("RemoteStorageEnumerateUserPublishedFilesResult_t %zu\n", sizeof(RemoteStorageEnumerateUserPublishedFilesResult_t));
    printf("RemoteStorageSubscribePublishedFileResult_t %zu\n", sizeof(RemoteStorageSubscribePublishedFileResult_t));
    printf("RemoteStorageEnumerateUserSubscribedFilesResult_t %zu\n", sizeof(RemoteStorageEnumerateUserSubscribedFilesResult_t));
    printf("RemoteStorageUnsubscribePublishedFileResult_t %zu\n", sizeof(RemoteStorageUnsubscribePublishedFileResult_t));
    printf("RemoteStorageUpdatePublishedFileResult_t %zu\n", sizeof(RemoteStorageUpdatePublishedFileResult_t));
    printf("RemoteStorageDownloadUGCResult_t %zu\n", sizeof(RemoteStorageDownloadUGCResult_t));
    printf("RemoteStorageGetPublishedFileDetailsResult_t %zu\n", sizeof(RemoteStorageGetPublishedFileDetailsResult_t));
    printf("RemoteStorageEnumerateWorkshopFilesResult_t %zu\n", sizeof(RemoteStorageEnumerateWorkshopFilesResult_t));
    printf("RemoteStorageGetPublishedItemVoteDetailsResult_t %zu\n", sizeof(RemoteStorageGetPublishedItemVoteDetailsResult_t));
    printf("RemoteStoragePublishedFileUpdated_t %zu\n", sizeof(RemoteStoragePublishedFileUpdated_t));
    printf("RemoteStorageUpdateUserPublishedItemVoteResult_t %zu\n", sizeof(RemoteStorageUpdateUserPublishedItemVoteResult_t));
    printf("RemoteStorageUserVoteDetails_t %zu\n",   sizeof(RemoteStorageUserVoteDetails_t));
    printf("RemoteStorageEnumerateUserSharedWorkshopFilesResult_t %zu\n", sizeof(RemoteStorageEnumerateUserSharedWorkshopFilesResult_t));
    printf("RemoteStorageSetUserPublishedFileActionResult_t %zu\n", sizeof(RemoteStorageSetUserPublishedFileActionResult_t));
    printf("RemoteStorageEnumeratePublishedFilesByUserActionResult_t %zu\n", sizeof(RemoteStorageEnumeratePublishedFilesByUserActionResult_t));
    printf("RemoteStoragePublishFileProgress_t %zu\n", sizeof(RemoteStoragePublishFileProgress_t));
    printf("RemoteStoragePublishedFileSubscribed_t %zu\n", sizeof(RemoteStoragePublishedFileSubscribed_t));
    printf("RemoteStoragePublishedFileUnsubscribed_t %zu\n", sizeof(RemoteStoragePublishedFileUnsubscribed_t));
    printf("RemoteStoragePublishedFileDeleted_t %zu\n", sizeof(RemoteStoragePublishedFileDeleted_t));
    printf("UserStatsReceived_t %zu\n",              sizeof(UserStatsReceived_t));
    printf("UserStatsStored_t %zu\n",                sizeof(UserStatsStored_t));
    printf("UserAchievementStored_t %zu\n",          sizeof(UserAchievementStored_t));
    printf("LeaderboardFindResult_t %zu\n",          sizeof(LeaderboardFindResult_t));
    printf("LeaderboardScoresDownloaded_t %zu\n",    sizeof(LeaderboardScoresDownloaded_t));
    printf("LeaderboardScoreUploaded_t %zu\n",       sizeof(LeaderboardScoreUploaded_t));
    printf("LeaderboardUGCSet_t %zu\n",              sizeof(LeaderboardUGCSet_t));
    printf("NumberOfCurrentPlayers_t %zu\n",         sizeof(NumberOfCurrentPlayers_t));
    printf("UserStatsUnloaded_t %zu\n",              sizeof(UserStatsUnloaded_t));
    printf("UserAchievementIconFetched_t %zu\n",     sizeof(UserAchievementIconFetched_t));
    printf("GlobalAchievementPercentagesReady_t %zu\n", sizeof(GlobalAchievementPercentagesReady_t));
    printf("GlobalStatsReceived_t %zu\n",            sizeof(GlobalStatsReceived_t));
    printf("SteamAPICallCompleted_t %zu\n",          sizeof(SteamAPICallCompleted_t));
    printf("SteamShutdown_t %zu\n",                  sizeof(SteamShutdown_t));
    printf("CheckFileSignature_t %zu\n",             sizeof(CheckFileSignature_t));
    printf("GamepadTextInputDismissed_t %zu\n",      sizeof(GamepadTextInputDismissed_t));
    printf("AppResumingFromSuspend_t %zu\n",         sizeof(AppResumingFromSuspend_t));
    printf("FloatingGamepadTextInputDismissed_t %zu\n", sizeof(FloatingGamepadTextInputDismissed_t));
    printf("FilterTextDictionaryChanged_t %zu\n",    sizeof(FilterTextDictionaryChanged_t));
    printf("SteamNetConnectionStatusChangedCallback_t %zu\n", sizeof(SteamNetConnectionStatusChangedCallback_t));
    printf("SteamNetAuthenticationStatus_t %zu\n",   sizeof(SteamNetAuthenticationStatus_t));
    printf("SteamRelayNetworkStatus_t %zu\n",        sizeof(SteamRelayNetworkStatus_t));
    printf("HTTPRequestCompleted_t %zu\n",           sizeof(HTTPRequestCompleted_t));
    printf("HTTPRequestHeadersReceived_t %zu\n",     sizeof(HTTPRequestHeadersReceived_t));
    printf("HTTPRequestDataReceived_t %zu\n",        sizeof(HTTPRequestDataReceived_t));
    printf("SteamInventoryResultReady_t %zu\n",      sizeof(SteamInventoryResultReady_t));
    printf("SteamInventoryFullUpdate_t %zu\n",       sizeof(SteamInventoryFullUpdate_t));
    printf("SteamInventoryDefinitionUpdate_t %zu\n", sizeof(SteamInventoryDefinitionUpdate_t));
    printf("SteamInventoryEligiblePromoItemDefIDs_t %zu\n", sizeof(SteamInventoryEligiblePromoItemDefIDs_t));
    printf("SteamInventoryStartPurchaseResult_t %zu\n", sizeof(SteamInventoryStartPurchaseResult_t));
    printf("SteamInventoryRequestPricesResult_t %zu\n", sizeof(SteamInventoryRequestPricesResult_t));
    printf("GetVideoURLResult_t %zu\n",              sizeof(GetVideoURLResult_t));
    printf("GetOPFSettingsResult_t %zu\n",           sizeof(GetOPFSettingsResult_t));
    printf("SteamRemotePlaySessionConnected_t %zu\n", sizeof(SteamRemotePlaySessionConnected_t));
    printf("SteamRemotePlaySessionDisconnected_t %zu\n", sizeof(SteamRemotePlaySessionDisconnected_t));
    printf("SteamRemotePlayTogetherGuestInvite_t %zu\n", sizeof(SteamRemotePlayTogetherGuestInvite_t));
    printf("SteamNetworkingMessagesSessionRequest_t %zu\n", sizeof(SteamNetworkingMessagesSessionRequest_t));
    printf("SteamNetworkingMessagesSessionFailed_t %zu\n", sizeof(SteamNetworkingMessagesSessionFailed_t));
    printf("SteamInputDeviceConnected_t %zu\n",      sizeof(SteamInputDeviceConnected_t));
    printf("SteamInputDeviceDisconnected_t %zu\n",   sizeof(SteamInputDeviceDisconnected_t));
    printf("SteamInputConfigurationLoaded_t %zu\n",  sizeof(SteamInputConfigurationLoaded_t));
    printf("SteamInputGamepadSlotChange_t %zu\n",    sizeof(SteamInputGamepadSlotChange_t));
    printf("SteamUGCQueryCompleted_t %zu\n",         sizeof(SteamUGCQueryCompleted_t));
    printf("SteamUGCRequestUGCDetailsResult_t %zu\n", sizeof(SteamUGCRequestUGCDetailsResult_t));
    printf("CreateItemResult_t %zu\n",               sizeof(CreateItemResult_t));
    printf("SubmitItemUpdateResult_t %zu\n",         sizeof(SubmitItemUpdateResult_t));
    printf("ItemInstalled_t %zu\n",                  sizeof(ItemInstalled_t));
    printf("DownloadItemResult_t %zu\n",             sizeof(DownloadItemResult_t));
    printf("UserFavoriteItemsListChanged_t %zu\n",   sizeof(UserFavoriteItemsListChanged_t));
    printf("SetUserItemVoteResult_t %zu\n",          sizeof(SetUserItemVoteResult_t));
    printf("GetUserItemVoteResult_t %zu\n",          sizeof(GetUserItemVoteResult_t));
    printf("StartPlaytimeTrackingResult_t %zu\n",    sizeof(StartPlaytimeTrackingResult_t));
    printf("StopPlaytimeTrackingResult_t %zu\n",     sizeof(StopPlaytimeTrackingResult_t));
    printf("AddUGCDependencyResult_t %zu\n",         sizeof(AddUGCDependencyResult_t));
    printf("RemoveUGCDependencyResult_t %zu\n",      sizeof(RemoveUGCDependencyResult_t));
    printf("AddAppDependencyResult_t %zu\n",         sizeof(AddAppDependencyResult_t));
    printf("RemoveAppDependencyResult_t %zu\n",      sizeof(RemoveAppDependencyResult_t));
    printf("GetAppDependenciesResult_t %zu\n",       sizeof(GetAppDependenciesResult_t));
    printf("DeleteItemResult_t %zu\n",               sizeof(DeleteItemResult_t));
    printf("UserSubscribedItemsListChanged_t %zu\n", sizeof(UserSubscribedItemsListChanged_t));
    printf("WorkshopEULAStatus_t %zu\n",             sizeof(WorkshopEULAStatus_t));

    // ── Plain structs ─────────────────────────────────────────────────────────
    printf("SteamIPAddress_t %zu\n",                 sizeof(SteamIPAddress_t));
    printf("FriendGameInfo_t %zu\n",                 sizeof(FriendGameInfo_t));
    printf("MatchMakingKeyValuePair_t %zu\n",        sizeof(MatchMakingKeyValuePair_t));
    printf("servernetadr_t %zu\n",                   sizeof(servernetadr_t));
    printf("SteamPartyBeaconLocation_t %zu\n",       sizeof(SteamPartyBeaconLocation_t));
    printf("P2PSessionState_t %zu\n",                sizeof(P2PSessionState_t));
    printf("InputAnalogActionData_t %zu\n",          sizeof(InputAnalogActionData_t));
    printf("InputDigitalActionData_t %zu\n",         sizeof(InputDigitalActionData_t));
    printf("InputMotionData_t %zu\n",                sizeof(InputMotionData_t));
    printf("SteamNetworkingIPAddr %zu\n",            sizeof(SteamNetworkingIPAddr));
    printf("SteamNetworkingIdentity %zu\n",          sizeof(SteamNetworkingIdentity));
    printf("SteamNetConnectionInfo_t %zu\n",         sizeof(SteamNetConnectionInfo_t));
    printf("SteamNetConnectionRealTimeStatus_t %zu\n", sizeof(SteamNetConnectionRealTimeStatus_t));
    printf("SteamNetConnectionRealTimeLaneStatus_t %zu\n", sizeof(SteamNetConnectionRealTimeLaneStatus_t));
    printf("SteamNetworkPingLocation_t %zu\n",       sizeof(SteamNetworkPingLocation_t));

    return 0;
}
