# GHPC-mods

MelonLoader mods for **Gunner, HEAT, PC!** by **rhino2024**, MIT licensed.

Each mod is packaged and released separately — take only the one you want.

| mod | what it adds | source | release tag | download |
| --- | --- | --- | --- | --- |
| [Slew Reticle](SlewReticle/README.md) | War Thunder style exterior aiming: a fixed screen circle that the turret slews to, hold-to-zoom, commander view slews without the circle | [`SlewReticle/src`](SlewReticle/src) | `SlewReticle-v1.0.1` | `GHPC-SlewReticle-1.0.1.zip` |
| [Hit Cam](HitCam/README.md) | a hit replay window in the corner for a few seconds after your shell lands, orbiting the struck vehicle with its interior damage | [`HitCam/src`](HitCam/src) | `HitCam-v1.0.1` | `GHPC-HitCam-1.0.1.zip` |
| [Repair Menu](RepairMenu/README.md) | a Repair submenu inside the game's own Q menu: pick the damaged module to fix, and the work takes real time | [`RepairMenu/src`](RepairMenu/src) | `v1.0.1` | `RhinoRepairMenu-1.0.1.zip` |
| [Wreck Cleanup](WreckCleanup/README.md) | switches burnt-out wrecks off once the field holds enough of them, so they stop costing frames | [`WreckCleanup/src`](WreckCleanup/src) | `v1.0.1` | `RhinoWreckCleanup-1.0.1.zip` |

All four are plain DLL mods: no AssetBundle, no game file is modified, uninstall by deleting one file.

Repair Menu and Wreck Cleanup are also in the GHPC Mod Manager directory, so the manager can install them -
see [manager/](manager/README.md).

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

The community directory is `GHPC.DMR.gg/config/modconfig.json` (source: `RoyZ-iwnl/GHPC-Mod-Manager-Web`,
`config/modconfig.json` + `config/mod_i18n.json`; a PR merged there is what makes an entry visible). The
paste-ready blocks live in [manager/](manager/README.md), which also explains the two mechanics that shape
them - confirmed from the manager's own source, not guessed:

* `ReleaseUrl` is parsed only for the owner and repository name, and the installable version is the
  repository's **newest** release, so the `/releases/tags/<tag>` tail makes no difference. One release
  therefore has to carry the assets of every registered mod, and `TargetFileNameKeyword` keeps the two
  entries from taking each other's zip.
* `InstallMethod: "DirectRelease"` unpacks the zip straight into `<GameRoot>/Mods`, where GameRoot is the
  game's `Bin` directory. The registered zips hold the dll flat at the root; the nested `GHPC-*.zip`
  archives are only for copying by hand.

`Conflicts` is left empty on purpose: it hard-fails the manager's launch check, and while `Repair Kit`
covers similar ground it has never been tested against this mod, so the overlap is described in the entry
text instead.

Hit Cam and Slew Reticle are not registered yet - they overlap with `GHPC Combat Overhaul` (kill cam,
War Thunder style HUD) on the same interior materials and cameras, and that pairing still needs testing
before an entry can honestly declare conflicts. Registering them later means adding their zips to the same
newest release, not publishing a separate one.

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
manager/README.md       how the directory entry works, and the two JSON blocks to paste
manager/modconfig-entries.json
manager/mod_i18n-entries.json
CHANGELOG.md
LICENSE
```

Release zips are not committed; they are attached to the GitHub releases.

## License

MIT — see [LICENSE](LICENSE). Not affiliated with Radian Simulations. Tested against the current Steam
build; a game update may break them.
