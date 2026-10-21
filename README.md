# GHPC-mods

MelonLoader mods for **Gunner, HEAT, PC!** by **rhino2024**, MIT licensed.

Each mod is packaged and released separately — take only the one you want.

| mod | what it adds | source | release tag | download |
| --- | --- | --- | --- | --- |
| [Slew Reticle](SlewReticle/README.md) | War Thunder style exterior aiming: a fixed screen circle that the turret slews to, in both exterior views | [`SlewReticle/src`](SlewReticle/src) | `SlewReticle-v1.0.0` | `GHPC-SlewReticle-1.0.0.zip` |
| [Hit Cam](HitCam/README.md) | a hit replay window in the corner for a few seconds after your shell lands, orbiting the struck vehicle with its interior damage | [`HitCam/src`](HitCam/src) | `HitCam-v1.0.0` | `GHPC-HitCam-1.0.0.zip` |

Both are plain DLL mods: no AssetBundle, no game file is modified, uninstall by deleting one file.

## Requirements

- Gunner, HEAT, PC! (Steam build), Unity 2022.3 + Mono runtime backend.
- [MelonLoader](https://github.com/lava-gang/MelonLoader) **v0.7.2** with the `net35` runtime — not
  included in the downloads.

## Install

1. Close the game completely (the DLLs are locked while it runs).
2. Extract the mod's zip into the game root, the folder that contains `GHPC.exe`:
   - `<game root>\Bin\Mods\SlewReticle.dll`
   - `<game root>\Bin\Mods\HitCam.dll`
3. Start the game once. MelonLoader writes a `[SlewReticle]` and/or `[HitCam]` section into
   `<game root>\Bin\UserData\MelonPreferences.cfg`, which is where all the tuning lives.
4. Uninstall by deleting the DLL.

## Quick reference

| | Slew Reticle | Hit Cam |
| --- | --- | --- |
| turns on | automatically, in any exterior view | automatically, after your shell hits something |
| keys | `G` kill switch, `Keypad +/-` move the circle up/down live | `F9` toggle, `F10` debug dump (needs `DebugLog`) |
| main tuning knob | `RingOffsetY`, `RingScale`, `RingColor` | `Duration`, `WindowWidth`/`WindowHeight`, `OrbitSpeed` |
| writes to disk | no, unless `DebugLog = true` | no, unless `DebugLog = true` |

Useful camera facts, measured from the running game rather than assumed: `LeftShift` switches between
first person and an exterior view, `C` switches between the commander view and the gunner third person
view, and the game keeps one main camera named `camera` for all of them.

## Building from source

No .NET SDK and no Unity editor needed — the mods are single DLLs compiled with any Roslyn `csc`
against the game's own managed assemblies:

```bash
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash SlewReticle/src/build.sh deploy
GHPC_DIR="/path/to/Gunner HEAT PC/Bin" bash HitCam/src/build.sh deploy
```

`GHPC_DIR` defaults to the developer's own install path; set it to yours. `deploy` requires the game to
be closed.

## Registering with GHPC Mod Manager

The community directory at `GHPC.DMR.gg/config/modconfig.json` points each entry at a GitHub release.
Because each mod here has its own tag, use the per-tag endpoint so the two mods never shadow each other:

```json
{
  "Id": "rhino2024-SlewReticle",
  "Name": "Slew Reticle",
  "Description": "War Thunder style exterior aiming: a fixed screen circle the turret slews to.",
  "Tags": ["ui", "gunner"],
  "SupportedGameVersions": ["Current"],
  "ReleaseUrl": "https://api.github.com/repos/rhino2024/GHPC-mods/releases/tags/SlewReticle-v1.0.0",
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
  "ReleaseUrl": "https://api.github.com/repos/rhino2024/GHPC-mods/releases/tags/HitCam-v1.0.0",
  "TargetFileNameKeyword": "HitCam",
  "MainBinaryFileName": "HitCam.dll",
  "ConfigSectionName": "HitCam",
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
CHANGELOG.md
LICENSE
```

Release zips are not committed; they are attached to the GitHub releases.

## License

MIT — see [LICENSE](LICENSE). Not affiliated with Radian Simulations. Tested against the current Steam
build; a game update may break them.
