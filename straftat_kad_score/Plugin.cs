using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using ComputerysModdingUtilities;
using HarmonyLib;
using UnityEngine;


[assembly: StraftatMod(true)]
namespace straftat_kad_score;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]

public class Plugin : BaseUnityPlugin
{
    internal new static ManualLogSource Logger;
    private static readonly Dictionary<int, int> KillsByPlayerId = new();
    private static readonly Dictionary<int, int> DeathsByPlayerId = new();
    private static readonly Dictionary<int, int> LastKillerByVictimId = new();
    private static readonly Dictionary<int, float> LastKillerTimeByVictimId = new();
    private static readonly Dictionary<int, float> LastDeathTimeByPlayerId = new();
    private static readonly Dictionary<int, int> LastDamagerByVictimId = new();
    private static readonly Dictionary<int, float> LastDamageTimeByVictimId = new();
    private static readonly Dictionary<int, int> LastKillFeedKillerByVictimId = new();
    private static readonly Dictionary<int, float> LastKillFeedTimeByVictimId = new();
    private static readonly Dictionary<string, float> LastProcessedKillFeedLineTime = new();
    private static readonly Dictionary<int, float> LastKillFeedAppliedDeathTimeByVictimId = new();
    private static bool hasLoggedVictoryStats;
    private static bool hasActiveMatch;
    private const float FallbackMaxDamageAgeSeconds = 5f;
    private static readonly bool UseKillFeedAsSourceOfTruth = true;
    private static readonly Regex PlayerTagRegex = new("\\{PLAYER_NAME\\}:\\{(\\d+)\\}", RegexOptions.Compiled);
    private static Sprite anclaSprite;

    public static void LogInfo(string message, bool writeOffline = false, string color = "white")
    {
        Logger.LogInfo(message);
        if (writeOffline)
            PauseManager.Instance.WriteOfflineLog($"<color={color}><b>{message}</b></color>");
    }

    public static void LogWarning(string message, bool writeOffline = false, string color = "yellow")
    {
        Logger.LogWarning(message);
        if (writeOffline)
            PauseManager.Instance.WriteOfflineLog($"<color={color}><b>{message}</b></color>");
    }

    public static void LogError(string message, bool writeOffline = false, string color = "red")
    {
        Logger.LogError(message);
        if (writeOffline)
            PauseManager.Instance.WriteOfflineLog($"<color={color}><b>ERROR: {message}</b></color>");
    }

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        new Harmony("straftat_kad_score").PatchAll();
    }

    public static void StartNewMatch()
    {
        Patches.VictoryMenuUiPatch.ResetState();
        KillsByPlayerId.Clear();
        DeathsByPlayerId.Clear();
        LastKillerByVictimId.Clear();
        LastKillerTimeByVictimId.Clear();
        LastDeathTimeByPlayerId.Clear();
        LastDamagerByVictimId.Clear();
        LastDamageTimeByVictimId.Clear();
        LastKillFeedKillerByVictimId.Clear();
        LastKillFeedTimeByVictimId.Clear();
        LastProcessedKillFeedLineTime.Clear();
        LastKillFeedAppliedDeathTimeByVictimId.Clear();
        hasLoggedVictoryStats = false;
        hasActiveMatch = true;
        LogInfo("[KAD] Match tracking started/reset.");
    }

    public static void ResetTracking()
    {
        Patches.VictoryMenuUiPatch.ResetState();
        KillsByPlayerId.Clear();
        DeathsByPlayerId.Clear();
        LastKillerByVictimId.Clear();
        LastKillerTimeByVictimId.Clear();
        LastDeathTimeByPlayerId.Clear();
        LastDamagerByVictimId.Clear();
        LastDamageTimeByVictimId.Clear();
        LastKillFeedKillerByVictimId.Clear();
        LastKillFeedTimeByVictimId.Clear();
        LastProcessedKillFeedLineTime.Clear();
        LastKillFeedAppliedDeathTimeByVictimId.Clear();
        hasLoggedVictoryStats = false;
        hasActiveMatch = false;
        LogInfo("[KAD] Match tracking fully reset (main menu/cleanup).");
    }

    public static void EnsureMatchTrackingStarted()
    {
        if (!hasActiveMatch || hasLoggedVictoryStats)
            StartNewMatch();
    }

    public static void RecordKiller(PlayerHealth victim, Transform killerTransform)
    {
        if (victim == null || !victim.IsServer)
            return;

        if (!TryGetPlayerId(victim, out var victimId))
        {
            LogWarning("[KAD] RecordKiller ignored: could not resolve victim id.");
            return;
        }

        if (!TryGetPlayerId(killerTransform, out var killerId))
        {
            LogWarning($"[KAD] RecordKiller ignored for victim {GetPlayerLabel(victimId)}: could not resolve killer id.");
            return;
        }

        LastKillerByVictimId[victimId] = killerId;
        LastKillerTimeByVictimId[victimId] = Time.time;
        LastDamagerByVictimId[victimId] = killerId;
        LastDamageTimeByVictimId[victimId] = Time.time;
        LogInfo($"[KAD] Killer mapped: victim={GetPlayerLabel(victimId)} <- killer={GetPlayerLabel(killerId)}");
    }

    public static void RecordDeath(int victimId)
    {
        if (UseKillFeedAsSourceOfTruth)
        {
            LogInfo($"[KAD] Server death hook ignored for {GetPlayerLabel(victimId)} (killfeed-authoritative mode).");
            return;
        }

        if (victimId < 0)
            return;

        if (LastKillFeedAppliedDeathTimeByVictimId.TryGetValue(victimId, out var killFeedAppliedTime) &&
            Time.time - killFeedAppliedTime < 1.5f)
        {
            LogInfo($"[KAD] Server death ignored for {GetPlayerLabel(victimId)}: already applied from killfeed.");
            return;
        }

        if (LastDeathTimeByPlayerId.TryGetValue(victimId, out var lastDeathTime) &&
            Time.time - lastDeathTime < 0.2f)
        {
            LogWarning($"[KAD] Death ignored as duplicate for {GetPlayerLabel(victimId)} (delta={Time.time - lastDeathTime:0.000}s).");
            return;
        }

        LastDeathTimeByPlayerId[victimId] = Time.time;
        DeathsByPlayerId[victimId] = DeathsByPlayerId.GetValueOrDefault(victimId, 0) + 1;
        LogInfo($"[KAD] Death +1: {GetPlayerLabel(victimId)} => D={DeathsByPlayerId[victimId]}");

        int killerId = -1;
        var killerSource = "none";

        if (TryGetFreshMappedKiller(victimId, out var mappedKillerId))
        {
            killerId = mappedKillerId;
            killerSource = "mapped";
        }
        else if (TryResolveKillerFromVictimState(victimId, out var victimStateKillerId))
        {
            killerId = victimStateKillerId;
            killerSource = "victim-state";
            LogInfo($"[KAD] Fallback killer recovered from victim state: victim={GetPlayerLabel(victimId)} <- killer={GetPlayerLabel(victimStateKillerId)}");
        }
        else if (TryGetFreshRecentDamager(victimId, out var recentDamagerId, out var ageSeconds))
        {
            killerId = recentDamagerId;
            killerSource = "recent-damager";
            LogInfo($"[KAD] Fallback killer recovered from recent damager: victim={GetPlayerLabel(victimId)} <- killer={GetPlayerLabel(recentDamagerId)} (age={ageSeconds:0.00}s)");
        }
        else if (TryGetFreshKillFeedKiller(victimId, out var killFeedKillerId, out var killFeedAge))
        {
            killerId = killFeedKillerId;
            killerSource = "killfeed";
            LogInfo($"[KAD] Fallback killer recovered from killfeed: victim={GetPlayerLabel(victimId)} <- killer={GetPlayerLabel(killFeedKillerId)} (age={killFeedAge:0.00}s)");
        }

        if (killerId >= 0)
        {
            if (killerId != victimId)
            {
                KillsByPlayerId[killerId] = KillsByPlayerId.GetValueOrDefault(killerId, 0) + 1;
                LogInfo($"[KAD] Kill +1: {GetPlayerLabel(killerId)} => K={KillsByPlayerId[killerId]} (victim {GetPlayerLabel(victimId)}, source={killerSource})");
            }
            else
            {
                LogInfo($"[KAD] Self-kill detected for {GetPlayerLabel(victimId)}: no kill awarded.");
            }
        }
        else
        {
            LogWarning($"[KAD] No killer mapping for victim {GetPlayerLabel(victimId)} at death time.");
        }

        LastKillerByVictimId.Remove(victimId);
        LastKillerTimeByVictimId.Remove(victimId);
        LastDamagerByVictimId.Remove(victimId);
        LastDamageTimeByVictimId.Remove(victimId);
        LastKillFeedKillerByVictimId.Remove(victimId);
        LastKillFeedTimeByVictimId.Remove(victimId);
        LogInfo($"[KAD] Snapshot after death: {BuildSnapshot()}");
    }

    public static void TryLogVictoryStats()
    {
        if (hasLoggedVictoryStats)
            return;

        hasLoggedVictoryStats = true;

        if (ClientInstance.playerInstances == null || ClientInstance.playerInstances.Count == 0)
        {
            LogInfo("VictoryScene reached. No players found for K/D summary.");
            return;
        }

        LogInfo("VictoryScene K/D summary:");
        foreach (var player in ClientInstance.playerInstances.Values)
        {
            var kills = KillsByPlayerId.GetValueOrDefault(player.PlayerId, 0);
            var deaths = DeathsByPlayerId.GetValueOrDefault(player.PlayerId, 0);
            LogInfo($"{player.PlayerName} (ID {player.PlayerId}) - K: {kills} D: {deaths}");
        }
    }

    private static bool TryGetPlayerId(Transform transform, out int playerId)
    {
        playerId = -1;
        if (transform == null)
            return false;

        var playerValues = transform.GetComponent<PlayerValues>() ??
                           transform.GetComponentInParent<PlayerValues>() ??
                           transform.GetComponentInChildren<PlayerValues>();
        if (TryGetPlayerId(playerValues?.playerClient, out playerId))
            return true;

        var clientInstance = transform.GetComponent<ClientInstance>() ??
                             transform.GetComponentInParent<ClientInstance>() ??
                             transform.GetComponentInChildren<ClientInstance>();
        return TryGetPlayerId(clientInstance, out playerId);
    }

    private static bool TryGetPlayerId(PlayerHealth playerHealth, out int playerId)
    {
        playerId = -1;
        if (playerHealth == null)
            return false;

        if (TryGetPlayerId(playerHealth.playerValues?.playerClient, out playerId))
            return true;

        return TryGetPlayerId(playerHealth.transform, out playerId);
    }

    private static bool TryGetPlayerId(ClientInstance clientInstance, out int playerId)
    {
        playerId = -1;
        if (clientInstance == null || clientInstance.PlayerId < 0)
            return false;

        playerId = clientInstance.PlayerId;
        return true;
    }

    private static bool TryResolveKillerFromVictimState(int victimId, out int killerId)
    {
        killerId = -1;
        var healths = Object.FindObjectsOfType<PlayerHealth>();
        for (var i = 0; i < healths.Length; i++)
        {
            var health = healths[i];
            if (health == null)
                continue;

            if (!TryGetPlayerId(health, out var playerId) || playerId != victimId)
                continue;

            if (!TryGetPlayerId(health.killer, out killerId))
                return false;

            if (!TryGetFreshRecentDamager(victimId, out var lastDamagerId, out var age))
            {
                LogWarning($"[KAD] Fallback rejected for {GetPlayerLabel(victimId)}: no fresh recent damage record.");
                return false;
            }

            if (lastDamagerId != killerId)
            {
                LogWarning($"[KAD] Fallback rejected for {GetPlayerLabel(victimId)}: killer state {GetPlayerLabel(killerId)} != last damager {GetPlayerLabel(lastDamagerId)}.");
                return false;
            }
            LogInfo($"[KAD] Victim-state fallback validated by recent damage age {age:0.00}s.");

            return true;
        }

        return false;
    }

    private static bool TryGetFreshMappedKiller(int victimId, out int killerId)
    {
        killerId = -1;
        if (!LastKillerByVictimId.TryGetValue(victimId, out var mappedKillerId))
            return false;

        if (!LastKillerTimeByVictimId.TryGetValue(victimId, out var mapTime))
        {
            LogWarning($"[KAD] Mapped killer missing timestamp for {GetPlayerLabel(victimId)}.");
            return false;
        }

        var age = Time.time - mapTime;
        if (age > FallbackMaxDamageAgeSeconds)
        {
            LogWarning($"[KAD] Mapped killer stale for {GetPlayerLabel(victimId)}: age {age:0.00}s > {FallbackMaxDamageAgeSeconds:0.00}s.");
            return false;
        }

        killerId = mappedKillerId;
        return true;
    }

    private static bool TryGetFreshRecentDamager(int victimId, out int damagerId, out float ageSeconds)
    {
        damagerId = -1;
        ageSeconds = float.MaxValue;
        if (!LastDamageTimeByVictimId.TryGetValue(victimId, out var lastDamageTime) ||
            !LastDamagerByVictimId.TryGetValue(victimId, out var recentDamagerId))
        {
            return false;
        }

        ageSeconds = Time.time - lastDamageTime;
        if (ageSeconds > FallbackMaxDamageAgeSeconds)
        {
            LogWarning($"[KAD] Recent damager stale for {GetPlayerLabel(victimId)}: age {ageSeconds:0.00}s > {FallbackMaxDamageAgeSeconds:0.00}s.");
            return false;
        }

        damagerId = recentDamagerId;
        return true;
    }

    private static bool TryGetFreshKillFeedKiller(int victimId, out int killerId, out float ageSeconds)
    {
        killerId = -1;
        ageSeconds = float.MaxValue;
        if (!LastKillFeedKillerByVictimId.TryGetValue(victimId, out var recentKillerId) ||
            !LastKillFeedTimeByVictimId.TryGetValue(victimId, out var lastFeedTime))
        {
            return false;
        }

        ageSeconds = Time.time - lastFeedTime;
        if (ageSeconds > FallbackMaxDamageAgeSeconds)
        {
            LogWarning($"[KAD] Killfeed fallback stale for {GetPlayerLabel(victimId)}: age {ageSeconds:0.00}s > {FallbackMaxDamageAgeSeconds:0.00}s.");
            return false;
        }

        killerId = recentKillerId;
        return true;
    }

    public static void RecordDamage(PlayerHealth victim, float damage)
    {
        if (victim == null || !victim.IsServer || damage <= 0f)
            return;

        if (!TryGetPlayerId(victim, out var victimId))
            return;

        if (!TryGetPlayerId(victim.killer, out var damagerId) || damagerId == victimId)
            return;

        LastDamagerByVictimId[victimId] = damagerId;
        LastDamageTimeByVictimId[victimId] = Time.time;
        LogInfo($"[KAD] Damage marker: victim={GetPlayerLabel(victimId)} <- damager={GetPlayerLabel(damagerId)} at t={Time.time:0.00}");
    }

    public static void RecordKillFeedLine(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (!PauseManager.Instance.inMainMenu)
            EnsureMatchTrackingStarted();

        if (TryApplyDeathOnlyFromKillFeed(text))
            return;

        if (!text.Contains(" by "))
            return;

        var matches = PlayerTagRegex.Matches(text);
        if (matches.Count < 2)
            return;

        if (!int.TryParse(matches[0].Groups[1].Value, out var victimId))
            return;
        if (!int.TryParse(matches[matches.Count - 1].Groups[1].Value, out var killerId))
            return;
        if (victimId == killerId)
            return;

        LastKillFeedKillerByVictimId[victimId] = killerId;
        LastKillFeedTimeByVictimId[victimId] = Time.time;
        LogInfo($"[KAD] Killfeed marker: victim={GetPlayerLabel(victimId)} <- killer={GetPlayerLabel(killerId)} at t={Time.time:0.00}");

        if (TryMarkKillFeedLineOnce(text))
        {
            DeathsByPlayerId[victimId] = DeathsByPlayerId.GetValueOrDefault(victimId, 0) + 1;
            KillsByPlayerId[killerId] = KillsByPlayerId.GetValueOrDefault(killerId, 0) + 1;
            LastKillFeedAppliedDeathTimeByVictimId[victimId] = Time.time;
            LogInfo($"[KAD] Killfeed applied: {GetPlayerLabel(killerId)} +K, {GetPlayerLabel(victimId)} +D");
        }
    }

    private static bool TryApplyDeathOnlyFromKillFeed(string text)
    {
        if (!(text.Contains("fell into the void") || text.Contains("commited suicide") || text.EndsWith(" died")))
            return false;

        var matches = PlayerTagRegex.Matches(text);
        if (matches.Count < 1)
            return false;

        if (!int.TryParse(matches[0].Groups[1].Value, out var victimId))
            return false;

        if (!TryMarkKillFeedLineOnce(text))
            return true;

        DeathsByPlayerId[victimId] = DeathsByPlayerId.GetValueOrDefault(victimId, 0) + 1;
        LastKillFeedAppliedDeathTimeByVictimId[victimId] = Time.time;
        LogInfo($"[KAD] Killfeed applied death-only: {GetPlayerLabel(victimId)} +D");
        return true;
    }

    private static bool TryMarkKillFeedLineOnce(string line)
    {
        var key = line.Trim();
        if (LastProcessedKillFeedLineTime.TryGetValue(key, out var lastTime) && Time.time - lastTime < 0.15f)
            return false;

        LastProcessedKillFeedLineTime[key] = Time.time;
        return true;
    }

    private static string GetPlayerLabel(int playerId)
    {
        if (ClientInstance.playerInstances != null && ClientInstance.playerInstances.TryGetValue(playerId, out var player))
            return $"{player.PlayerName}#{playerId}";

        return $"ID#{playerId}";
    }

    private static string BuildSnapshot()
    {
        var allPlayerIds = KillsByPlayerId.Keys
            .Union(DeathsByPlayerId.Keys)
            .Union(ClientInstance.playerInstances?.Keys ?? Enumerable.Empty<int>())
            .OrderBy(x => x);

        var sb = new StringBuilder();
        var first = true;
        foreach (var playerId in allPlayerIds)
        {
            if (!first)
                sb.Append(" | ");
            first = false;

            sb.Append(GetPlayerLabel(playerId));
            sb.Append(":K");
            sb.Append(KillsByPlayerId.GetValueOrDefault(playerId, 0));
            sb.Append("/D");
            sb.Append(DeathsByPlayerId.GetValueOrDefault(playerId, 0));
        }

        return sb.Length == 0 ? "(empty)" : sb.ToString();
    }

    public static List<int> GetVictoryOrderedPlayerIds()
    {
        var result = new List<int>();
        if (ScoreManager.Instance == null)
            return result;

        var teams = new List<int>(ScoreManager.Instance.TeamIdToPlayerIds.Keys);
        teams.Sort((a, b) => ScoreManager.Instance.GetPoints(b).CompareTo(ScoreManager.Instance.GetPoints(a)));

        for (var i = 0; i < teams.Count; i++)
        {
            var players = ScoreManager.Instance.GetPlayerIdsForTeam(teams[i]);
            for (var j = 0; j < players.Count; j++)
                result.Add(players[j]);
        }

        return result;
    }

    public static void GetPlayerStats(int playerId, out int kills, out int deaths)
    {
        kills = KillsByPlayerId.GetValueOrDefault(playerId, 0);
        deaths = DeathsByPlayerId.GetValueOrDefault(playerId, 0);
    }

    public static bool TryGetWorstKdPlayerId(out int playerId)
    {
        playerId = -1;
        var playerIds = ClientInstance.playerInstances?.Keys?.ToList() ?? new List<int>();
        if (playerIds.Count == 0)
            return false;

        playerIds.Sort((a, b) =>
        {
            GetPlayerStats(a, out var aKills, out var aDeaths);
            GetPlayerStats(b, out var bKills, out var bDeaths);

            var ratioCompare = ComputeKdRatio(aKills, aDeaths).CompareTo(ComputeKdRatio(bKills, bDeaths));
            if (ratioCompare != 0)
                return ratioCompare;

            var deathsCompare = bDeaths.CompareTo(aDeaths);
            if (deathsCompare != 0)
                return deathsCompare;

            var killsCompare = aKills.CompareTo(bKills);
            if (killsCompare != 0)
                return killsCompare;

            return a.CompareTo(b);
        });

        playerId = playerIds[0];
        return true;
    }

    private static double ComputeKdRatio(int kills, int deaths)
    {
        if (deaths <= 0)
            return kills > 0 ? double.PositiveInfinity : 0d;
        return (double)kills / deaths;
    }

    public static Sprite GetAnclaSprite()
    {
        if (anclaSprite != null)
            return anclaSprite;

        const string resourceName = "straftat_kad_score.assets.ancla.png";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            LogError($"[KAD] Embedded resource not found: {resourceName}");
            return null;
        }

        byte[] bytes;
        using (var memoryStream = new MemoryStream())
        {
            stream.CopyTo(memoryStream);
            bytes = memoryStream.ToArray();
        }

        var cachePath = Path.Combine(Application.temporaryCachePath, "straftat_kad_score_ancla.png");
        File.WriteAllBytes(cachePath, bytes);
        var www = new WWW("file://" + cachePath);
        while (!www.isDone)
        {
        }

        var texture = www.texture;
        if (texture == null)
        {
            LogError("[KAD] Failed to decode embedded ancla image.");
            return null;
        }

        anclaSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        return anclaSprite;
    }
}
