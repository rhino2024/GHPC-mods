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

## One repository per entry

Confirmed from the manager's source, not guessed:

* `GetRepoOwner()` / `GetRepoName()` read only the owner and repository segments of `ReleaseUrl`, and the
  installable version is `releases.FirstOrDefault()?.TagName`, i.e. the repository's newest release.
  `/releases/latest` and `/releases/tags/<tag>` therefore behave the same.
* `DownloadModPackageAsync()` looks that tag up in the same list and picks the asset with
  `Assets.FirstOrDefault(a => a.Name.Contains(TargetFileNameKeyword))`.

Each entry now points at its own repository (`rhino2024/GHPC-RepairMenu`, `rhino2024/GHPC-WreckCleanup`),
so `releases/latest` of that repository always resolves to a release built for this entry. Keep it that
way: one release, one asset. `TargetFileNameKeyword` is `.zip`, a substring test that succeeds on the
single `GHPC-<name>-<version>.zip` asset; a second asset would make the choice depend on asset order.

## Zip layout

`InstallMethod: "DirectRelease"` extracts the archive into `<GameRoot>/Mods`, where the GameRoot the
manager asks for is the game's `Bin` folder. The registered zips hold `README.txt` plus
`Bin/Mods/<dll>`, and `ExtractArchiveAsync(detectDllRoot: true)` strips the directory of the first `.dll`
entry, so the files land as `Mods/<dll>` and `Mods/README.txt`.

## Locale slots and identity

`Name` / `Description` carry separate `en-US` and `zh-CN` text; both slots are filled because the manager
picks one by client locale. `Author` is set, and `SupportedGameVersions` lists `20261014`, the build the
mods were tested on. The ASCII identifiers (`Id`, `TargetFileNameKeyword`, `MainBinaryFileName`,
`ConfigSectionName`) stay English because they are matched against file names and the
`MelonPreferences.cfg` section.

## Labels and comments

`ConfigLabels` covers every setting. `ConfigComments` keys are the English comment strings from the mod
source verbatim - the manager looks a comment up by that text, so renaming a setting means updating both
the label key and the comment key.

## `Conflicts` is omitted

A listed conflict makes the manager's launch check report **Failed** rather than warn. `Repair Kit` covers
similar ground to 载具维修菜单, but the two have never been run against each other, so no conflict is
asserted.

## Not registered yet

命中回放小窗 (`HitCam`) and 第三人称瞄准环 (`SlewReticle`) have no entry. If they are added, each one gets
its own repository and its own entry, following the same pattern.
