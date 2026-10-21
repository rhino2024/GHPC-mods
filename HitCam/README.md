# Hit Cam

Author **rhino2024** · MIT · part of the [GHPC-mods](../README.md) collection.

After your shell hits something, a small window opens in the top-right corner and orbits the struck
vehicle for a few seconds, showing the impact point and the interior damage, then closes by itself so
you can keep shooting. It answers the "what just killed that tank" question without leaving the fight.

## What it does not do

- It never enters the game's own post-mission AAR, never pauses or disables physics, never moves other
  vehicles, and never modifies any real vehicle's loadout or scorch state.
- It does not turn your hull transparent. The interior you see is the game's own AAR artwork
  (`AarVisual`), lit up and rendered by the mod's own private cameras into a RenderTexture, so the
  main view is untouched.
- Nothing is written to disk unless you set `DebugLog = true`.

## Keys

| key | what it does |
| --- | --- |
| `F9` | toggle the mod at runtime |
| `F10` | write an AAR state dump to the debug log — only responds while `DebugLog = true` |

## Install

1. Close the game.
2. Extract the download into the game root, the folder containing `GHPC.exe`, so that you get
   `<game root>\Bin\Mods\HitCam.dll`.
3. Start the game. MelonLoader creates the `[HitCam]` section in
   `<game root>\Bin\UserData\MelonPreferences.cfg` on the first run.

Requires MelonLoader **v0.7.2** (net35 runtime). It is not included.

## Configuration (`[HitCam]`)

| key | default | meaning |
| --- | --- | --- |
| `Enabled` | `true` | master switch |
| `ToggleKey` | `F9` | runtime toggle |
| `ConfigRevision` | `0` | internal marker for one-time config migrations; leave it alone |
| `Duration` | `3` | seconds the window stays open |
| `WindowWidth` / `WindowHeight` | `640` / `360` | replay image size in pixels |
| `Margin` | `24` | distance from the top-right corner, pixels |
| `MinCaliberMm` | `40` | ignore shells below this calibre |
| `OrbitSpeed` | `26` | camera orbit speed, degrees per second |
| `Elevation` | `10` | camera angle above the target, degrees |
| `DistanceScale` | `0.72` | how far the replay camera sits from the target |
| `FieldOfView` | `24` | replay camera FOV |
| `ShowInterior` | `true` | light up the struck vehicle's interior art in the window |
| `ShowInfoText` | `true` | ammo / distance / penetration caption |
| `ShowFlightPath` | `false` | keep the long gun-to-target flight trace visible |
| `FlightLegMeters` | `25` | trace legs longer than this are treated as the flight path and hidden |
| `DebugLog` | `false` | write diagnostics to `Bin/Mods/HitCam.debug.log` |

## How it works

One Harmony postfix on the game's own shell-termination routine (`GHPC.Weapons.LiveRound.doDestroy`),
which is where `ShotInfo` is complete: hit models, penetration frames, stop position and the game's own
narrative string. Everything else is read-only reflection over that result. The window is built from
two private cameras sharing one RenderTexture — a world camera plus a depth-only overlay camera that
draws the AAR art on top of the hull — mirroring how the game builds its own replay view, with the
vehicle's own follow roots (`HULL FOLLOW` / `TURRET FOLLOW`) located by world-space bounds so it works
on vehicles that name them differently. Interior parts are switched on through the game's own
`AarVisual` highlighting and restored when the window closes.

The window is a second full-scene render, so it is deliberately cheap: it redraws at most every
33 ms (30 Hz, which is plenty for a slow orbit), runs with MSAA off, and only lives for `Duration`
seconds. If it ever costs you more than you want, lower `WindowWidth` / `WindowHeight`.

## Known limits

- The interior view depends on the game's own `AarVisual` art; a few vehicles may show an incomplete
  interior simply because the art is not there for the mod to light up.
- The window is cosmetic: it never delays or replaces your next shot, and hits fired while a window is
  open queue up behind it.
- Firing and then immediately entering the post-mission AAR is the least exercised path — the window is
  built to hand the view back as soon as `AarController.InAar` goes true, but if you ever see it stick
  around during the real AAR, set `DebugLog = true`, reproduce it, and open an issue with the log.

## Building

```bash
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash src/build.sh          # writes bin/HitCam.dll
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash src/build.sh deploy   # also copies into $GHPC_DIR/Mods
```

Needs any Roslyn `csc` (the game is Unity Mono, no .NET SDK required) and the game's own
`GHPC_Data/Managed/*.dll` as references, plus `0Harmony.dll` and `Tomlet.dll` from
`Bin/MelonLoader/net35`. Modules required beyond the core ones: `IMGUIModule`, `InputLegacyModule`,
`PhysicsModule`, `ParticleSystemModule`, `VehiclesModule`.

## 中文说明

命中后右上角开小窗，镜头绕着被击中的载具转几秒，看弹着点和舱内损伤，到时间自动关闭，不耽误继续打。
**不进赛后 AAR、不改物理、不动别人的车**；舱内画面是游戏自己的 AAR 素材由 mod 的私有相机叠加出来的，
不是把外壳变透明。`F9` 开关，`F10` 写探针（需先开 `DebugLog`，默认关，不会往硬盘写日志）。

## License

MIT — see [LICENSE](../LICENSE). Not affiliated with Radian Simulations; built and tested against the
current Steam build and may break on a game update.
