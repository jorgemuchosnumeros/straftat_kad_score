using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace straftat_kad_score.Patches;

[HarmonyPatch(typeof(VictoryMenuUI), "Start")]
public static class VictoryMenuUiPatch
{
	private static bool applyCompleted;
	private static bool applyRoutineRunning;

	public static void ResetState()
	{
		applyCompleted = false;
		applyRoutineRunning = false;
	}

	public static void Postfix(VictoryMenuUI __instance)
	{
		TryApply(__instance, "VictoryMenuUI.Start");
	}

	public static void TryApply(MonoBehaviour host, string source)
	{
		if (host == null || applyCompleted || applyRoutineRunning)
			return;

		if (SceneManager.GetActiveScene().name != "VictoryScene")
			return;

		applyRoutineRunning = true;
		Plugin.LogInfo($"[KAD] Victory UI patch: trigger source={source}");
		host.StartCoroutine(ApplyUiWithRetries());
	}

	private static IEnumerator ApplyUiWithRetries()
	{
		for (var frame = 0; frame < 180; frame++)
		{
			yield return null;

			if (SceneManager.GetActiveScene().name != "VictoryScene")
				break;

			if (TryApplyUiOnce())
			{
				applyCompleted = true;
				applyRoutineRunning = false;
				yield break;
			}
		}

		applyRoutineRunning = false;
		Plugin.LogWarning("[KAD] Victory UI patch: timed out before UI was found.");
	}

	private static bool TryApplyUiOnce()
	{
		if (SceneManager.GetActiveScene().name != "VictoryScene")
			return false;

		var orderedPlayerIds = Plugin.GetVictoryOrderedPlayerIds();
		if (orderedPlayerIds.Count == 0)
			return false;

		var cellHolder = GameObject.Find("VICTORY SCREENS/CellHolder") ?? FindObjectByName("CellHolder");
		if (cellHolder == null)
			return false;

		var cells = cellHolder.GetComponentsInChildren<Transform>(true)
			.Where(t => t.name == "VictoryCell(Clone)")
			.OrderBy(t => t.GetSiblingIndex())
			.ToList();
		if (cells.Count == 0)
			return false;

		Plugin.LogInfo($"[KAD] Victory UI patch: cells={cells.Count}, orderedPlayers={orderedPlayerIds.Count}");

		var rowCount = Mathf.Min(cells.Count, orderedPlayerIds.Count);
		for (var i = 0; i < rowCount; i++)
		{
			Plugin.GetPlayerStats(orderedPlayerIds[i], out var kills, out var deaths);
			AppendStatsText(cells[i], kills, deaths);
		}

		if (!Plugin.TryGetWorstKdPlayerId(out var worstPlayerId))
		{
			Plugin.LogWarning("[KAD] Victory UI patch: could not determine worst K/D player.");
			return true;
		}

		var worstIndex = orderedPlayerIds.IndexOf(worstPlayerId);
		if (worstIndex < 0 || worstIndex >= cells.Count)
		{
			Plugin.LogWarning($"[KAD] Victory UI patch: worst player index invalid ({worstIndex}).");
			return true;
		}

		Plugin.LogInfo($"[KAD] Victory UI patch: applying worst badge to row={worstIndex}, playerId={worstPlayerId}.");
		ApplyWorstBadge(cells[worstIndex]);
		return true;
	}

	private static void AppendStatsText(Transform cell, int kills, int deaths)
	{
		var statsTransform = FindInChildrenByName(cell, "StatsPlayerOne");
		if (statsTransform == null)
			return;

		var tmpComponent = statsTransform.GetComponents<MonoBehaviour>()
			.FirstOrDefault(c => c != null && c.GetType().Name.Contains("TextMeshPro"));
		if (tmpComponent == null)
		{
			Plugin.LogWarning("[KAD] Victory UI patch: StatsPlayerOne has no TextMeshPro component.");
			return;
		}

		var kdText = $"K/D: {kills}/{deaths}";
		var textProperty = tmpComponent.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
		if (textProperty == null || textProperty.PropertyType != typeof(string))
		{
			Plugin.LogWarning("[KAD] Victory UI patch: TextMeshPro component has no string text property.");
			return;
		}

		var currentText = (string)textProperty.GetValue(tmpComponent, null) ?? string.Empty;
		if (currentText.Contains(kdText))
			return;

		textProperty.SetValue(tmpComponent, $"{currentText} | {kdText}", null);
		Plugin.LogInfo($"[KAD] Victory UI patch: appended '{kdText}'");
	}

	private static void ApplyWorstBadge(Transform cell)
	{
		var fancyTransform = FindInChildrenByName(cell, "fancy");
		if (fancyTransform == null)
		{
			Plugin.LogWarning("[KAD] Victory UI patch: could not find 'fancy' object.");
			return;
		}

		fancyTransform.gameObject.SetActive(true);
		for (var i = 0; i < fancyTransform.childCount; i++)
			fancyTransform.GetChild(i).gameObject.SetActive(i == 0);

		if (fancyTransform.childCount == 0)
			return;

		var crown = fancyTransform.GetChild(0);
		var spriteRenderer = crown.GetComponent<SpriteRenderer>();
		if (spriteRenderer == null)
		{
			Plugin.LogWarning("[KAD] Victory UI patch: fancy child 0 has no SpriteRenderer.");
			return;
		}

		var sprite = Plugin.GetAnclaSprite();
		if (sprite != null)
		{
			spriteRenderer.sprite = sprite;
			SetGlobalScaleXY(crown, 0.15f, 0.15f);
			Plugin.LogInfo("[KAD] Victory UI patch: ancla badge applied.");
		}
		else
		{
			Plugin.LogWarning("[KAD] Victory UI patch: ancla sprite is null.");
		}
	}

	private static Transform FindInChildrenByName(Transform parent, string objectName)
	{
		var children = parent.GetComponentsInChildren<Transform>(true);
		for (var i = 0; i < children.Length; i++)
		{
			if (children[i].name == objectName)
				return children[i];
		}

		return null;
	}

	private static GameObject FindObjectByName(string objectName)
	{
		var allTransforms = Object.FindObjectsOfType<Transform>(true);
		for (var i = 0; i < allTransforms.Length; i++)
		{
			if (allTransforms[i].name == objectName)
				return allTransforms[i].gameObject;
		}

		return null;
	}

	private static void SetGlobalScaleXY(Transform target, float globalX, float globalY)
	{
		var parent = target.parent;
		if (parent == null)
		{
			var s = target.localScale;
			target.localScale = new Vector3(globalX, globalY, s.z);
			return;
		}

		var parentScale = parent.lossyScale;
		var s2 = target.localScale;
		var localX = Mathf.Approximately(parentScale.x, 0f) ? globalX : globalX / parentScale.x;
		var localY = Mathf.Approximately(parentScale.y, 0f) ? globalY : globalY / parentScale.y;
		target.localScale = new Vector3(localX, localY, s2.z);
	}
}
