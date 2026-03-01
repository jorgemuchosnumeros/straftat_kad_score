using HarmonyLib;

namespace straftat_kad_score.Patches;

[HarmonyPatch(typeof(MatchLogs), "RpcLogic___RpcSendChatLineToAllObservers_3615296227")]
public static class MatchLogsReceiverPatch
{
	public static void Prefix(string line)
	{
		Plugin.RecordKillFeedLine(line, true);
	}
}
