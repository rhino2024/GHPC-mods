# GHPC-mods

MelonLoader mods for **Gunner, HEAT, PC!** by **rhino2024**, MIT licensed.

Each mod is packaged and released separately — take only the one you want.

| mod | what it adds | source | release tag | download |
| --- | --- | --- | --- | --- |
| [Slew Reticle](SlewReticle/README.md) | War Thunder style exterior aiming: a fixed screen circle that the turret slews to, hold-to-zoom, commander view slews without the circle | [`SlewReticle/src`](SlewReticle/src) | `SlewReticle-v1.0.1` | `GHPC-SlewReticle-1.0.1.zip` |
| [Hit Cam](HitCam/README.md) | a hit replay window in the corner for a few seconds after your shell lands, orbiting the struck vehicle with its interior damage | [`HitCam/src`](HitCam/src) | `HitCam-v1.0.1` | `GHPC-HitCam-1.0.1.zip` |
| [Repair Menu](RepairMenu/README.md) | a Repair submenu inside the game's own Q menu: pick the damaged module to fix, and the work takes real time | [`RepairMenu/src`](RepairMenu/src) | `RepairMenu-v1.0.0` | `GHPC-RepairMenu-1.0.0.zip` |
| [Wreck Cleanup](WreckCleanup/README.md) | switches burnt-out wrecks off once the field holds enough of them, so they stop costing frames | [`WreckCleanup/src`](WreckCleanup/src) | `WreckCleanup-v1.0.0` | `GHPC-WreckCleanup-1.0.0.zip` |

All four are plain DLL mods: no AssetBundle, no game file is modified, uninstall by deleting one file.

## Requirements

- Gunner, HEAT, PC! (Steam build), Unity 2022.3 + Mono runtime backend.
- [MelonLoader](https://github.com/lava-gang/MelonLoader) **v0.7.2** with the `net35` runtime — not
  included in the downloads.

## Install

1. Close the game completely (the DLLs are locked while it runs).
2. Extract the mod's zip into the game root, the folder that contains `GHPC.exe`:
   - `<game root>\Bin\Mods\SlewReticle.dll`
   - `<game root>\Bin\Mods\HitCam.dll`
   - `<game root>\Bin\Mods\RepairMenu.dll`
   - `<game root>\Bin\Mods\WreckCleanup.dll`
3. Start the game once. MelonLoader writes a `[SlewReticle]`, `[HitCam]`, `[RepairMenu]` and
   `[WreckCleanup]` section into `<game root>\Bin\UserData\MelonPreferences.cfg`, which is where all the
   tuning lives.
4. Uninstall by deleting the DLL.

## Quick reference

| | what to remember |
| --- | --- |
| Slew Reticle | on in any exterior view; `G` kill switch, hold `Z` or the mouse right button to zoom, `Keypad +/-` move the circle live; commander view has no circle but still slews |
| Hit Cam | on after your shell hits something; `F9` toggle, `F10` debug dump (needs `DebugLog`) |
| Repair Menu | press the game's own `Q`, open `Repair`; a full repair costs `RepairSeconds` (30 s default), one module costs its share |
| Wreck Cleanup | automatic once `WreckThreshold` (3 default) wrecks are on the field, after they have burned `WreckDelay` (8 s); `F8` toggle |

None of them write anything to disk unless you set `DebugLog = true` in its config section.

Useful camera facts, measured from the running game rather than assumed: `LeftShift` switches between
first person and an exterior view, `C` switches between the commander view and the gunner third person
view, and the game keeps one main camera named `camera` for all of them.

## Building from source

No .NET SDK and no Unity editor needed — the mods are single DLLs compiled with any Roslyn `csc`
against the game's own managed assemblies:

```bash
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash SlewReticle/src/build.sh deploy
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash HitCam/src/build.sh deploy
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash RepairMenu/src/build.sh deploy
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash WreckCleanup/src/build.sh deploy
```

`GHPC_DIR` defaults to the developer's own install path; set it to yours. `deploy` requires the game to
be closed.

## Registering with GHPC Mod Manager

The community directory at `GHPC.DMR.gg/config/modconfig.json` points each entry at a GitHub release.
Because each mod here has its own tag, use the per-tag endpoint so the mods never shadow each other:

```json
{
  "Id": "rhino2024-SlewReticle",
  "Name": "Slew Reticle",
  "Description": "War Thunder style exterior aiming: a fixed screen circle the turret slews to.",
  "Tags": ["ui", "gunner"],
  "SupportedGameVersions": ["Current"],
  "ReleaseUrl": "https://api.github.com/repos/rhino2024/GHPC-mods/releases/tags/SlewReticle-v1.0.1",
  "TargetFileNameKeyword": "SlewReticle",
  "MainBinaryFileName": "SlewReticle.dll",
  "ConfigSectionName": "SlewReticle",
  "InstallMethod": "Manually"
}
```

```json
{
  "Id": "rhino2024-HitCam",
  "Name": "Hit Cam",
  "Description": "A short hit replay window in the corner, orbiting the struck vehicle with interior damage.",
  "Tags": ["ui", "aar"],
  "SupportedGameVersions": ["Current"],
  "ReleaseUrl": "https://api.github.com/repos/rhino2024/GHPC-mods/releases/tags/HitCam-v1.0.1",
  "TargetFileNameKeyword": "HitCam",
  "MainBinaryFileName": "HitCam.dll",
  "ConfigSectionName": "HitCam",
  "InstallMethod": "Manually"
}
```

```json
{
  "Id": "rhino2024-RepairMenu",
  "Name": "Repair Menu",
  "Description": "A Repair submenu in the game's own Q menu: pick the damaged chassis module and fix it, with a real repair time.",
  "Tags": ["ui", "damage"],
  "SupportedGameVersions": ["Current"],
  "ReleaseUrl": "https://api.github.com/repos/rhino2024/GHPC-mods/releases/tags/RepairMenu-v1.0.0",
  "TargetFileNameKeyword": "RepairMenu",
  "MainBinaryFileName": "RepairMenu.dll",
  "ConfigSectionName": "RepairMenu",
  "InstallMethod": "Manually"
}
```

```json
{
  "Id": "rhino2024-WreckCleanup",
  "Name": "Wreck Cleanup",
  "Description": "Switches burnt-out wrecks off once the field holds enough of them, so they stop costing frames.",
  "Tags": ["performance"],
  "SupportedGameVersions": ["Current"],
  "ReleaseUrl": "https://api.github.com/repos/rhino2024/GHPC-mods/releases/tags/WreckCleanup-v1.0.0",
  "TargetFileNameKeyword": "WreckCleanup",
  "MainBinaryFileName": "WreckCleanup.dll",
  "ConfigSectionName": "WreckCleanup",
  "InstallMethod": "Manually"
}
```

Bump the tag in the URL when you publish a new release, or the directory will keep offering the old one.

## Repository layout

```
SlewReticle/README.md   per-mod documentation
SlewReticle/src/        source + build script
HitCam/README.md
HitCam/src/
RepairMenu/README.md
RepairMenu/src/
WreckCleanup/README.md
WreckCleanup/src/
CHANGELOG.md
LICENSE
```

Release zips are not committed; they are attached to the GitHub releases.

## License

MIT — see [LICENSE](LICENSE). Not affiliated with Radian Simulations. Tested against the current Steam
build; a game update may break them.
