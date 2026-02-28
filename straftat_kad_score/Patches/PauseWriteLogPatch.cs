using HarmonyLib;

namespace straftat_kad_score.Patches;

[HarmonyPatch(typeof(PauseManager), nameof(PauseManager.WriteLog))]
public static class PauseWriteLogPatch
{
	public static void Prefix(string text)
	{
		Plugin.RecordKillFeedLine(text);
	}
}
