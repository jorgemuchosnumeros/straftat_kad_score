using HarmonyLib;
using UnityEngine.SceneManagement;

namespace straftat_kad_score.Patches;

[HarmonyPatch(typeof(PauseManager), "Update")]
public static class ReturnToMenuScenePatch
{
	public static void Prefix(PauseManager __instance)
	{
		if (!__instance.inMainMenu && SceneManager.GetActiveScene().name == "MainMenu")
		{
			Plugin.ResetTracking();
		}
	}
}
