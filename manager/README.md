# GHPC Mod Manager entries

These are the paste-ready blocks for the community directory that
[GHPC Mod Manager](https://github.com/RoyZ-iwnl/GHPC-Mod-Manager) reads from
`https://GHPC.DMR.gg/config/modconfig.json`.

| file | goes into |
| --- | --- |
| [`modconfig-entries.json`](modconfig-entries.json) | `config/modconfig.json` - append the two objects to the top level array |
| [`mod_i18n-entries.json`](mod_i18n-entries.json) | `config/mod_i18n.json` - merge the two objects into `ModConfigs` |

Pushing those two files to `RoyZ-iwnl/GHPC-Mod-Manager-Web` (fork + PR) is what makes an entry visible to
everyone; a GitHub Action deploys `config/**` on merge.

## Why both entries point at one release

Confirmed from the manager's source, not guessed:

* `Services/Mod/ModManagerService.cs` - `GetRepoOwner()` / `GetRepoName()` read only the owner and
  repository segments of `ReleaseUrl`, so `/releases/tags/<tag>` behaves the same as `/releases/latest`:
  the version the browser lists is `releases.FirstOrDefault()?.TagName`, i.e. the newest release of the
  repository.
* `Services/Mod/ModManagerService.Replace.cs` - `DownloadModPackageAsync()` then looks that tag up in the
  same list and picks the asset with `Assets.FirstOrDefault(a => a.Name.Contains(TargetFileNameKeyword))`.

So one repository cannot drive two entries with two separate releases: whichever one is newest would be
the only one the other entry can reach, and its asset would not match the other keyword.
**The newest release of this repository must carry every zip the directory references** - right now
`RhinoRepairMenu-1.0.1.zip` and `RhinoWreckCleanup-1.0.1.zip` together in one release tagged `v1.0.1`.
`TargetFileNameKeyword` is what keeps the two entries from taking each other's file, which is why the zip
names are unique rather than plain `.zip`.

## Zip layout

`InstallMethod: "DirectRelease"` unpacks straight into `<GameRoot>/Mods`, and the GameRoot the manager asks
for is the game's `Bin` folder. So the registered zips hold `RepairMenu.dll` / `WreckCleanup.dll` **flat at
the zip root** - the manually downloadable `GHPC-*.zip` archives keep the nested `Bin/Mods/…` layout that
suits copy-it-yourself installs, and extracting those through the manager would produce
`Mods/Bin/Mods/…` and load nothing.

## `Conflicts` is left empty on purpose

A listed conflict makes the manager's launch check report **Failed** rather than warn. `Repair Kit` covers
similar ground to Repair Menu, but the two have never been run against each other, so the overlap is
described in the entry text instead of asserted as a conflict.

## Not registered yet

Hit Cam and Slew Reticle overlap `GHPC Combat Overhaul` (X-ray kill cam, War Thunder style HUD) on the same
interior materials and the same camera objects. Until that combination is actually tested, an entry for
them could not honestly state whether it conflicts. Adding them later means putting their zips into the
same newest release.
