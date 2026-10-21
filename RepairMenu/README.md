# Repair Menu

Author **rhino2024** · MIT · part of the [GHPC-mods](../README.md) collection.

Puts a **Repair** submenu inside the game's own **Q** context menu. It lists the chassis modules that
are actually damaged on your vehicle and repairs the one you pick — or all of them — and the work then
takes real time instead of happening instantly.

## Behaviour

- The submenu only appears while something on the vehicle is damaged, so a healthy tank shows exactly
  the menu it always had.
- Each entry carries its live state: `Repair/Engine (34%, 8 s)`. `Repair/All (18 s)` repairs every
  damaged module at once.
- **The repair takes time, and a full repair never costs more than your budget.** Modules are weighted
  so all five together come to exactly `RepairSeconds` (default 30), and a single module costs its share
  of it — the engine 8/30, a track 5/30, the radiator 4/30. Set `RepairSeconds = 0` for the instant fix.
- While the timer runs, the module health climbs back towards 100 % so you can see the work happening,
  and a line at the bottom of the screen reads `Repairing Engine - 12 s left`.
- Only one job at a time. Reopening **Q** during a repair gives you `Repair/… - N s left, click to
  cancel` instead of a second one; cancelling keeps whatever health was already recovered.
- The job is abandoned if the vehicle is destroyed, if you leave it, or when the scene changes.
- Repairing is more than refilling health. A destroyed engine leaves the vehicle without power and
  stops the NWH engine, a killed transmission overwrites the gearbox type with a manual one, and the
  chassis keeps its own "broken" and parking-brake state, so when the timer is up the mod restores
  those too and refreshes the game's damage text.
- Mobility is never cleared early: the shared "broken" state is only released when the engine and
  **both** tracks are healthy again, so repairing the radiator cannot make a trackless tank drivable.

| key | what it does |
| --- | --- |
| `Q` | the game's own context menu — this mod adds its submenu to it, nothing else to remember |

## Install

1. Close the game.
2. Extract the download into the game root, the folder containing `GHPC.exe`, so that you get
   `<game root>\Bin\Mods\RepairMenu.dll`.
3. Start the game. MelonLoader creates the `[RepairMenu]` section in
   `<game root>\Bin\UserData\MelonPreferences.cfg` on the first run.

Requires MelonLoader **v0.7.2** (net35 runtime). It is not included.

## Configuration (`[RepairMenu]`)

| key | default | meaning |
| --- | --- | --- |
| `RepairSeconds` | `30` | what a repair of every module costs; one module takes its share. `0` = instant |
| `ShowProgress` | `true` | draw the remaining time at the bottom of the screen |
| `DebugLog` | `false` | write diagnostics to `Bin/Mods/RepairMenu.debug.log` |

## How it works

The game rebuilds the Q menu from scratch every time you open it, so the mod hangs one Harmony prefix
on `GHPC.Player.PlayerInput.FinalizeContextMenu` — the point after all of its own `Add*Buttons` calls
and before it sorts the entries — and pushes buttons through the game's own `ContextMenuBuilder`. The
entries are ordinary context menu items and vanish with the rest when the menu closes.

Damage lives on `GHPC.Vehicle.ChassisDamageManager`: `EngineHitZone`, `TransmissionHitZone`,
`RadiatorHitZone`, `LeftTrackHitZone`, `RightTrackHitZone`, each a
`GHPC.Equipment.DestructibleComponent` whose `SetHealthPercent` fires the game's own repair events. The
rest is the bookkeeping the game does not give back: its private damaged/destroyed flags, the NWH
`Engine.Start`, the transmission type `KillTransmission` overwrote, the power state `KillPower` dropped,
and `NwhChassis`' private broken/parking-brake fields.

## Known limits

- Five chassis modules only — engine, transmission, radiator, left and right track. Weapons, optics and
  crew have no repair callback or internal state to restore, so faking them would leave a vehicle that
  looks fixed and is not.
- No cost and no crew requirement: this is a workshop switch, not a logistics system. You can repair
  while driving and you spend no parts.
- Damage flags and power state are written through reflection on private fields. A game update can move
  them, in which case the repair still refills health but may leave one of the extra symptoms.

## 中文说明

按 **Q** 打开游戏自带的小菜单，里面多出一个 **Repair** 子菜单，只在你有模块受损时出现，条目带当前血量
和所需秒数，比如 `Repair/Engine (34%, 8 s)`，也可以 `Repair/All`。
**修复需要等待**：五个模块全修正好等于预算 `RepairSeconds`（默认 30 秒），单修一个只占它那份
（发动机 8/30、履带 5/30、散热器 4/30），设 0 就是瞬间修完。倒计时期间血量一点点爬回 100%，屏幕底部
显示剩余秒数；同一时刻只有一个修车任务，再按 Q 会看到 `… click to cancel`。
时间到了才做那些"光回血修不好"的部分：损管文字、断电、变速箱档位、发动机启动、底盘停驶/手刹状态。
只有发动机和两条履带都健康时才会解除停驶，所以只修散热器不会让断履带的车跑起来。
车被击毁、离开该车或换场景，任务自动作废。`DebugLog` 默认关，不会往硬盘写日志。

## License

MIT — see [LICENSE](../LICENSE). Not affiliated with Radian Simulations; built and tested against the
current Steam build and may break on a game update.
