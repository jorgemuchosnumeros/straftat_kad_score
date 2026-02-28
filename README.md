# Straftat K/D Template (STRAFTAT / BepInEx Mono)

This mod tracks match K/D from the in-game match log stream and augments the Victory scoreboard.

## Installation (manual)
Assuming [BepInEx Mono](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_mono.html) is installed, unzip the release in `STRAFTAT/Bepinex/Plugins`.

## Current behavior
- Uses `PauseManager.WriteLog` and `MatchLogs` observer receive path as authoritative K/D sources.
- Parses kill/death log lines and updates in-memory per-player stats during the match.
- Supports mid-match joins (counts from the moment the player joins and starts receiving logs).
- Handles rematches by resetting internal state between rounds/matches.
- On Victory:
  - appends `K/D: kills/deaths` to each `StatsPlayerOne` text in `VictoryCell(Clone)`.
  - identifies worst K/D player and enables `fancy` for that row.
  - disables all `fancy` children except child `0` (`Vector-Crown-PNG-Image-Transparent-Background (1)`).
  - swaps that child sprite with embedded `assets/ancla.png` and sets global scale `x=0.15`, `y=0.15`.

## Notes
- K/D is intentionally killfeed-authoritative to avoid inconsistent RPC timing across host/client/mid-match states.
- Existing debug logs are prefixed with `[KAD]`.

## Building

Place required game assemblies in `straftat_kad_score/libs`:
- `Assembly-CSharp.dll`
- `ComputerysModdingUtilities.dll`
- `FishNet.Runtime.dll`
- `UnityEngine.dll`
- `UnityEngine.CoreModule.dll`
- `Unity.TextMeshPro.dll`
- `UnityEngine.UnityWebRequestModule.dll`
- `UnityEngine.UnityWebRequestWWWModule.dll`
- `UnityEngine.JSONSerializeModule.dll`

Then build:

```bash
dotnet build straftat_kad_score.sln
```
