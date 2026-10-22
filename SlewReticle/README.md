# 第三人称瞄准环 (`SlewReticle`)

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
- **Commander view gets the turret slew without the circle.** A commander's cupola is meant to be
  looked over rather than aimed through, so the circle is suppressed there and the aim ray leaves from
  the true screen centre instead. Set `HideRingInCommander = false` to get the old behaviour back.
- **Hold `Z` or the mouse right button to zoom the exterior view in**, release to go back. This runs
  the game's own zoom steps, so the zoom sound, the optic coupling and the FOV events behave exactly as
  they do with the mouse wheel.
- Because the follow camera puts your hull across the true screen centre, the circle sits a little
  above it. **Hold `Keypad +` to raise the circle, `Keypad -` to lower it.** It moves live and the
  value is written to the config file, so tuning does not need a restart.
- A ray that clips your own hull is discarded and the aim falls back to a far point along the ray, so
  the turret can never get stuck on its own barrel.
- Switching crew member does not break it: the game hands the gunner's station to the AI when you move
  to another seat, and an AI gunner aims the same fire control system from its own update. The mod
  parks the gunner AI while it is driving the aim and gives it back, sweep state included, when it
  stops.

| key | what it does |
| --- | --- |
| `G` | kill switch for the whole feature (it does not switch cameras) |
| `Z` (hold) | zoom the exterior view in |
| mouse right button (hold) | same zoom; switch off with `ZoomOnMouse = false` if it clashes with a binding of yours |
| `Keypad +` / `Keypad -` | raise / lower the circle, live, auto-saved |

Camera mapping measured from the running game (the game's own state flags, not assumed):

| key | effect |
| --- | --- |
| `LeftShift` | first person ↔ exterior view |
| `C` | commander view ↔ gunner third person |

The circle appears in the gunner third person view; commander view keeps the slew only.

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
| `HideRingInCommander` | `true` | commander view keeps the turret slew but draws no circle and aims from the true screen centre |
| `ZoomKey` | `Z` | hold to pull the exterior view in, release to go back to the base zoom |
| `ZoomOnMouse` | `true` | the mouse right button does the same hold-to-zoom |
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

The zoom is the game's own call as well: `BufferedCameraFollow.AdjustZoom(zoomIn, maxOut)` with the
`maximum` flag set, which lands on the strongest zoom level of the active camera slot and back on its
base level when the key goes up. The mod owns no camera, no FOV and no RenderTexture.

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
- Releasing the zoom key returns the camera to zoom level 0. A level you had scrolled to beforehand is
  not remembered.
- Camera slots the game gives free zoom (binocular-style views) take the game's own binocular path,
  where the `Z` hold can legitimately do nothing.

## 中文说明

进入外部视角就自动随动：**炮手第三人称**显示一个**固定不动的绿色圆圈**，炮塔转向圆圈指的方位；
**车长视角**不画圆圈但照样跟着视角转，射线走真屏幕中心。离开外部视角、用双筒、进自由相机或赛后 AAR
时自动关闭。圆圈与瞄准射线同源，射线打到自己车体会被忽略。**按住小键盘 `+` 上移圆圈、`-` 下移**，
实时生效并自动写入配置。**按住 `Z` 或鼠标右键放大**，松开回到基准档。`G` 是整功能开关。
换乘员后炮塔不跟了也修好了：游戏换座位时会把炮手 AI 打开并抢走瞄准权，mod 在工作期间先把它按住、
结束时按原样交还。
实测键位：`LeftShift` ＝ 第一人称↔外部视角，`C` ＝ 车长视角↔炮手第三人称。
`DebugLog` 默认关，不会往硬盘写日志。

## License

MIT — see [LICENSE](../LICENSE). Not affiliated with Radian Simulations; built and tested against the
current Steam build and may break on a game update.
