using HarmonyLib;
using UnityEngine.SceneManagement;

namespace straftat_kad_score.Patches;

[HarmonyPatch(typeof(PauseManager), "Update")]
public static class VictoryUiFallbackPatch
{
	public static void Postfix(PauseManager __instance)
	{
		var sceneName = SceneManager.GetActiveScene().name;

		if (!__instance.inMainMenu && !__instance.inVictoryMenu && sceneName != "VictoryScene")
			Plugin.EnsureMatchTrackingStarted();

		if (!__instance.inVictoryMenu)
			return;

		VictoryMenuUiPatch.TryApply(__instance, "PauseManager.Update");
	}
}
