# Changelog

Tags are per mod, so each mod can be released on its own: `SlewReticle-v1.0.0`, `HitCam-v1.0.0`.

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
