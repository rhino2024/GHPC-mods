# Changelog

Tags are per mod for the source (`SlewReticle-v1.0.1`, `HitCam-v1.0.1`, `RepairMenu-v1.0.1`,
`WreckCleanup-v1.0.1`). The GHPC Mod Manager reads the repository's **newest** release, so that one release
has to carry the zips of every registered mod - see [manager/README.md](manager/README.md).

## 载具维修菜单 1.0.2 / 残骸清理 1.0.2 — 2026-10-22

- **Names are Chinese now, everywhere.** The display name in `MelonInfo` and the preferences section title
  are 载具维修菜单 and 残骸清理, and the directory entries put that Chinese text in both locale slots, so the
  manager shows it whatever language the client runs in.
- **No `Rhino` prefix any more.** The directory `Id`s, the asset keyword and the zip names are
  `RepairMenu` / `WreckCleanup` and `GHPC-<mod>-1.0.2.zip`; `ConfigSectionName` and the DLL names are
  unchanged, so an existing `[RepairMenu]` / `[WreckCleanup]` config section keeps being read.
- Behaviour is otherwise identical to 1.0.1. The `MelonPreferences.cfg` section identifiers did not change.

## Repair Menu 1.0.1 — 2026-10-22

- **The Repair submenu survives dying, crew changes and a new vehicle.** The game's `ContextMenuRoot` is a
  single shared object that only `ContextMenuBuilder.Clear` empties, and the death / new-vehicle paths can
  skip that call - so the old "is there already a Repair node?" guard could latch permanently and the
  entries never came back. The mod now keeps a signature of the unit plus the labels it added: same
  signature means it only re-registers the button count, a different one destroys the stale node with
  `DestroyImmediate` (the builder adopts children by name in the same frame) and rebuilds.
- Two vehicles of the same type damaged in the same places no longer share rows: the unit is part of the
  signature, so a menu cannot keep pointing at the vehicle the player left.
- A healthy vehicle with nothing to offer leaves no trace at all instead of an empty Repair submenu.
- While a repair is running, another vehicle's menu shows `Repair/busy on another vehicle` rather than
  offering a second job.
- Every silent early return now logs with `DebugLog = true`, so a missing menu is diagnosable from
  `Bin/Mods/RepairMenu.debug.log` instead of guessed at.

## Wreck Cleanup 1.0.1 — 2026-10-22

- **Wrecks are now detected the way the game detects them.** The first build only looked at
  `Unit.Destroyed`, which GHPC sets for helicopters alone; a burnt-out ground vehicle goes through
  `Unit.Neutralized` - the same flag the casualty report and the tactical map read. With `Destroyed` only,
  a field of five burning hulks counted as zero wrecks and nothing was ever cleared. Both flags are now
  accepted.
- The sweep line gained `vehicles=` next to `units= / wrecks= / ready= / hidden=`, so which stage came up
  empty is readable at a glance.

## Slew Reticle 1.0.1 — 2026-10-21

- **Commander view no longer draws the circle.** It keeps the turret slew and aims along the true screen
  centre instead, which is what a commander's view is for. `HideRingInCommander = false` restores the
  circle there.
- **Hold `Z` or the mouse right button to zoom the exterior view in**, release to go back. Runs the
  game's own `BufferedCameraFollow.AdjustZoom`, so the zoom steps, sound and optic coupling are the
  game's. New settings: `ZoomKey`, `ZoomOnMouse`.
- Fixed the turret ignoring the circle after you switch crew member: the game re-enables every crew
  brain on a seat change, and a re-enabled gunner AI aims the same fire control system from its own
  update. The mod now parks the gunner AI while it owns the aim and hands it back — sweep state
  included — when it stops or you change seats.

## Hit Cam 1.0.1 — 2026-10-21

- Window default is back down to **640 × 360**; the replay costs a second full-scene render, so it is now
  drawn at most every 33 ms (30 Hz) with MSAA off.
- One-time config migration (`ConfigRevision`) moves an existing `800 × 450` section over to the new
  default instead of keeping the old size forever.
- The caption no longer invents English words: it shows the ammo designation plus plain numbers
  (distance, penetration) and lets the game's own shot narrative line state the damage, so it comes out
  in your game language like the AAR does.

## Repair Menu 1.0.0 — 2026-10-21

First public release.

- A **Repair** submenu inside the game's own **Q** context menu, added through a Harmony prefix on
  `PlayerInput.FinalizeContextMenu` and the game's own `ContextMenuBuilder`, so the entries are ordinary
  menu items in the game's own ordering.
- Lists only what is actually damaged on your vehicle — engine, transmission, radiator, left and right
  track — each with its live health, plus `Repair/All`.
- **Repairs take time.** Weights add up so a full repair costs exactly `RepairSeconds` (default 30) and a
  single module costs its share; `RepairSeconds = 0` gives the instant fix back. Health climbs during the
  wait, a bottom-of-screen line shows the remaining seconds, and one more `Q` opens a cancel option.
- More than health: the game's damage flags and damage text, the gearbox type a killed transmission
  overwrote, the stopped NWH engine, the power state a destroyed engine dropped, and the chassis' own
  broken / parking-brake state are restored when the timer runs out.
- Mobility is not cleared early: the shared broken state is only released once the engine and both tracks
  are healthy, so fixing the radiator cannot set a trackless tank driving.

## Wreck Cleanup 1.0.0 — 2026-10-21

First public release.

- Once the field holds `WreckThreshold` (default 3) destroyed vehicles, every wreck older than
  `WreckDelay` (default 8 s) is switched off, so its rendering, fire and smoke particles and physics stop.
- Wrecks are deactivated rather than destroyed: GHPC rebuilds its unit lists only when they are empty, so
  destroying a unit would leave the manager and AI targets holding dead objects.
- Ground vehicles and aircraft only; your own wreck is never touched; wrecks are marked `HideOnMap` so no
  stale icon is left on the tactical map.
- Hidden wrecks are switched back on for the duration of the after action replay, which films them, and
  cleaned again afterwards.
- `F8` toggles the cleanup mid-mission. Sweeps run once per second and stop while paused.

## Play-test status

**Repair Menu 1.0.1 and Wreck Cleanup 1.0.1 have been driven in game** and are the builds registered with
the GHPC Mod Manager directory.

Slew Reticle 1.0.3 (camera parked at the turret, white ring, muzzle-direction ring) and Hit Cam 1.0.2
(black background, pre-impact slow motion, health-based colouring) are built and deployed locally but have
not been confirmed in game yet, so they are not registered and no tag points at them. They get fixed
forward rather than by moving a published tag.

## Slew Reticle 1.0.0 — 2026-10-21

First public release.

- A fixed circle on screen whenever the game is in an exterior camera (commander view or gunner third
  person), with the turret slewing to whatever the circle is over. Switches itself off when you leave
  the exterior view, raise binoculars, enter free cam, or open the post-mission AAR.
- The circle and the aim ray share one coordinate, so the gun always points at what the circle covers.
- Rays that clip your own hull are discarded and the aim falls back to a far point along the ray, so
  the turret cannot get stuck on its own barrel.
- `RingOffsetY` lifts the circle above the true screen centre, because the follow camera parks your
  hull there. `Keypad +` / `Keypad -` adjust it live and save the value.
- `G` is a session kill switch for the whole feature.
- Drives the game's own aim chain (`CrewBrainWeaponsModule.SetAimPosition` → `FireControlSystem`), so
  no control-system patching and no camera movement.
- `DebugLog` off by default: no log file, and the diagnostic scene scans are skipped.

## Hit Cam 1.0.0 — 2026-10-21

First public release.

- After your shell hits something, a small window opens in the top-right corner and orbits the struck
  vehicle for a few seconds showing the impact and the interior damage, then closes on its own.
- Interior is drawn with the game's own `AarVisual` art through two private cameras (world camera plus
  a depth-only overlay camera) into one RenderTexture — the hull is never made transparent and the main
  view is untouched.
- Captions use the game's own shot narrative string plus ammo / distance / penetration figures.
- Long gun-to-target flight traces are hidden by default (`ShowFlightPath`), since the game builds the
  first trace leg as the whole trajectory.
- Never enters the post-mission AAR, never touches physics, never modifies other vehicles; yields the
  view as soon as the game's own AAR starts.
- `F9` toggle, `F10` debug dump. `DebugLog` off by default.
