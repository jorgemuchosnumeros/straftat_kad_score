using HarmonyLib;

namespace straftat_kad_score.Patches;

[HarmonyPatch(typeof(MenuController), nameof(MenuController.OpenGame))]
public static class OpenGamePatch
{
	public static void Prefix(MenuController __instance)
	{
		Plugin.StartNewMatch();
	}
}
