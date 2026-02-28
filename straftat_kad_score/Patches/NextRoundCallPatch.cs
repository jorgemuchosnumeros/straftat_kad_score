using HarmonyLib;
using UnityEngine.SceneManagement;

namespace straftat_kad_score.Patches;

[HarmonyPatch(typeof(PlayerManager), "WaitForRoundStartCoroutineStart")]
public static class NextRoundCallPatch
{
	public static void Prefix(PlayerManager __instance)
	{
		if (SceneManager.GetActiveScene().name == "VictoryScene" || PauseManager.Instance.inVictoryMenu)
		{
			Plugin.TryLogVictoryStats();
		}
		else if (!PauseManager.Instance.inMainMenu)
		{
			Plugin.EnsureMatchTrackingStarted();
		}
	}
}
