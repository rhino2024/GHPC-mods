# Slew Reticle

Author **rhino2024** · MIT · part of the [GHPC-mods](../README.md) collection.

A War Thunder style aiming aid for *Gunner, HEAT, PC!*. As soon as the game is in one of its
**exterior cameras**, a fixed circle appears on screen and your turret automatically slews to
whatever that circle is over. No mode key to press, no view to configure.

## Behaviour

- Turns itself **on** when the player enters an exterior view, and **off** when you leave it, raise
  binoculars, enter free cam, or open the post-mission AAR.
- The circle stays fixed on screen; it does not follow the mouse.
- The circle and the aim ray share one coordinate: the ray leaves from the circle's pixel, so the gun
  always points at what the circle covers.
- A ray that clips your own hull is discarded and the aim falls back to a far point along the ray, so
  the turret can never get stuck on its own barrel.
- Because the follow camera puts your hull across the true screen centre, the circle sits a little
  above it. **Hold `Keypad +` to raise the circle, `Keypad -` to lower it.** It moves live and the
  value is written to the config file, so tuning does not need a restart.

| key | what it does |
| --- | --- |
| `G` | kill switch for the whole feature (it does not switch cameras) |
| `Keypad +` / `Keypad -` | raise / lower the circle, live, auto-saved |

Camera mapping measured from the running game (the game's own state flags, not assumed):

| key | effect |
| --- | --- |
| `LeftShift` | first person ↔ exterior view |
| `C` | commander view ↔ gunner third person |

The circle appears in both exterior views.

## Install

1. Close the game.
2. Extract the download into the game root, the folder containing `GHPC.exe`, so that you get
   `<game root>\Bin\Mods\SlewReticle.dll`.
3. Start the game. MelonLoader creates the `[SlewReticle]` section in
   `<game root>\Bin\UserData\MelonPreferences.cfg` on the first run.

Requires MelonLoader **v0.7.2** (net35 runtime). It is not included.

## Configuration (`[SlewReticle]`)

| key | default | meaning |
| --- | --- | --- |
| `ToggleKey` | `G` | kill switch for the whole feature |
| `AutoSlew` | `true` | slew the turret automatically |
| `ShowRing` | `true` | draw the circle |
| `RingScale` | `0.055` | circle diameter as a fraction of screen height |
| `RingWidth` | `3` | circle line width in pixels |
| `RingColor` | `4DE673` | circle colour, RRGGBB |
| `RingOffsetY` | `0.18` | how far above the true screen centre the circle sits (fraction of screen height); `Keypad +/-` change it live and save it |
| `AimDistance` | `2000` | how far the ray pretends to reach when it hits nothing |
| `DebugLog` | `false` | write diagnostics to `Bin/Mods/SlewReticle.debug.log` |

`DebugLog` is off by default: nothing is written to disk and the diagnostic scene scans are skipped
entirely. Turn it on only while reproducing a problem, and note that for a few seconds after a mission
loads you will see `no player root transform` / `brain=0` — the player vehicle is not spawned yet,
which is normal.

## How it works

It drives the game's own aim chain instead of patching controls:
`GHPC.Player.PlayerInput` → `CurrentPlayerCrewBrain.WeaponsModule.SetAimPosition(role, worldPoint)` →
`FireControlSystem.SetAimWorldPosition(point)`. The screen centre ray comes from
`CameraManager.MainCam`. Everything is polled state, no Harmony patch, so it stops doing anything the
moment the exterior view is gone.

## Building

```bash
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash src/build.sh          # writes bin/SlewReticle.dll
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash src/build.sh deploy   # also copies into $GHPC_DIR/Mods
```

Needs any Roslyn `csc` (the game is Unity Mono, no .NET SDK required) and the game's own
`GHPC_Data/Managed/*.dll` as references, including `UnityEngine.IMGUIModule.dll` — `UnityEngine.GUI`
lives there, and the circle is drawn in `OnGUI`.

## Known limits

- Only the turret moves. The third person camera framing stays exactly what the game chose, so on a
  tall vehicle you may want to raise the circle a few more clicks.
- The circle position is a screen-space constant; it is not per-vehicle.

## 中文说明

进入外部视角（车长或炮手第三人称都算）就自动出现一个**固定不动的绿色圆圈**，炮塔自动转向圆圈指的方位；
离开外部视角、用双筒、进自由相机或赛后 AAR 时自动关闭。圆圈与瞄准射线同源，射线打到自己车体会被忽略。
**按住小键盘 `+` 上移圆圈、`-` 下移**，实时生效并自动写入配置。`G` 是整功能开关。
实测键位：`LeftShift` ＝ 第一人称↔外部视角，`C` ＝ 车长视角↔炮手第三人称。
`DebugLog` 默认关，不会往硬盘写日志。

## License

MIT — see [LICENSE](../LICENSE). Not affiliated with Radian Simulations; built and tested against the
current Steam build and may break on a game update.
