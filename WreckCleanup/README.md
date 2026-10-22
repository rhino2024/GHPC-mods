# 残骸清理 (`WreckCleanup`)

Author **rhino2024** · MIT · part of the [GHPC-mods](../README.md) collection.

Burning hulls pile up during a fight and each one costs frames: the wreck is still rendered, still
emitting fire and smoke, still simulated. This mod watches the field and, once enough wrecks have
accumulated, switches them off — the default is three.

## Behaviour

- It is a **threshold**, not a trickle. Below `WreckThreshold` nothing is touched at all; the moment the
  field holds that many destroyed vehicles, every wreck older than the grace period is cleared in one go.
- A wreck has to burn for `WreckDelay` seconds (default 8) first, so a kill is still watchable and a hit
  replay window can finish.
- Only vehicles are counted: ground vehicles and aircraft. Infantry, corpses and static emplacements are
  left alone.
- Your own wrecked vehicle is never hidden — you may still be inside it, bailing out of it, or watching
  it.
- **Wrecks are deactivated, not destroyed.** GHPC rebuilds its unit lists only when they are empty, so a
  `Destroy()` would leave the manager and every AI that has that vehicle as a target holding a dead
  object and throw. Switching the game object off keeps every reference valid and simply takes the unit
  out of the scene's active list; rendering, particles and physics all stop, which is where the frames
  are.
- Wrecks this mod hid are marked `HideOnMap` so they do not leave a stale icon on the tactical map.
- During the after action replay the hidden wrecks are switched **back on** — the AAR films the vehicles
  that were lost — and they are cleaned again afterwards.
- `F8` toggles the whole thing mid-mission and saves the choice.

| key | what it does |
| --- | --- |
| `F8` | enable / disable the cleanup for the session |

## Install

1. Close the game.
2. Extract the download into the game root, the folder containing `GHPC.exe`, so that you get
   `<game root>\Bin\Mods\WreckCleanup.dll`.
3. Start the game. MelonLoader creates the `[WreckCleanup]` section in
   `<game root>\Bin\UserData\MelonPreferences.cfg` on the first run.

Requires MelonLoader **v0.7.2** (net35 runtime). It is not included.

## Configuration (`[WreckCleanup]`)

| key | default | meaning |
| --- | --- | --- |
| `Enabled` | `true` | master switch |
| `WreckThreshold` | `3` | how many vehicle wrecks have to be on the field before they are cleared |
| `WreckDelay` | `8` | seconds a wreck must burn before it is allowed to be cleared |
| `CheckInterval` | `1` | seconds between sweeps |
| `HideOnMap` | `true` | also hide the wreck's tactical map icon |
| `RestoreInAar` | `true` | show the wrecks again while the AAR runs, since it films them |
| `ToggleKey` | `F8` | runtime on/off |
| `DebugLog` | `false` | write sweep diagnostics to `Bin/Mods/WreckCleanup.debug.log` |

A sweep that costs a second of work per second of game time is the whole overhead; it does not run while
paused, and the per-sweep log line exists only with `DebugLog = true`.

## How it works

No Harmony patch. Each sweep lists the `GHPC.Unit` components in the scene, keeps the ones the game itself
counts as lost - `Unit.Neutralized`, the same flag the casualty report and the tactical map read, plus
`Unit.Destroyed` for the cases where it is set separately - and whose `Type` is a ground or air vehicle.
`Destroyed` alone is not enough: the game only sets it for helicopters, while a burnt-out ground vehicle
goes through `Neutralized`. It times how long each wreck has been dead from the moment the mod first saw
it, and calls `SetActive(false)` on the ones that qualify. It remembers the units it hid so the AAR can
hand them back.

## Known limits

- Memory is not reclaimed. The frames come from rendering, particles and physics, all of which stop; the
  assets stay loaded because the game has no safe path to unload a unit mid-mission.
- Wrecks disappear rather than fading out, and a wreck you were looking at can be gone on the next sweep
  once the threshold is met. Raise `WreckDelay` or `WreckThreshold` if that is too abrupt.
- A mission that relies on inspecting a wreck afterwards may want this off; the AAR case is handled, an
  in-battle replay of a specific vehicle is not.
- Infantry casualties and destroyed emplacements are deliberately untouched.

## 中文说明

场上被击毁的**载具**残骸到 **3** 个就一次性清掉（可调），燃烧的车体一直在吃帧：渲染、火焰烟雾粒子、物理
全都还在跑。清掉＝把 GameObject 关掉，这三样立刻停止；**不是 Destroy**，因为游戏的单位列表只在空的时候
重建，销毁会让 AI 和任务判定持有失效对象而报错，关掉则引用依然有效。
残骸至少先烧 8 秒才允许被清，保证击杀后效和命中回放看得完；你自己那辆车的残骸永远不动。
隐藏时顺手 `HideOnMap`，免得战术地图留幽灵图标；**赛后 AAR 期间自动把残骸还原显示**（回放要拍它们），
AAR 结束再按规则重新清理。`F8` 随时开关。`DebugLog` 默认关，不写日志。

## License

MIT — see [LICENSE](../LICENSE). Not affiliated with Radian Simulations; built and tested against the
current Steam build and may break on a game update.
