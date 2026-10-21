#!/bin/bash
# Wreck Cleanup build script: Roslyn csc from VS2022 BuildTools, same as the other mods.
set -e

# Set GHPC_DIR / CSC if your install lives somewhere else.
BIN="${GHPC_DIR:-/e/steam/steamapps/common/Gunner HEAT PC/Bin}"
MGD="$BIN/GHPC_Data/Managed"
ML="$BIN/MelonLoader/net35"
CSC="${CSC:-/c/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/Roslyn/csc.exe}"
SRC="$(cd "$(dirname "$0")" && pwd)"
OUT="$SRC/bin"

mkdir -p "$OUT"

"$CSC" -nologo -noconfig -target:library -optimize+ -langversion:latest \
  -out:"$OUT/WreckCleanup.dll" \
  -r:System.dll \
  -r:System.Core.dll \
  -r:"$MGD/netstandard.dll" \
  -r:"$ML/MelonLoader.dll" \
  -r:"$ML/Tomlet.dll" \
  -r:"$MGD/UnityEngine.dll" \
  -r:"$MGD/UnityEngine.CoreModule.dll" \
  -r:"$MGD/UnityEngine.InputLegacyModule.dll" \
  -r:"$MGD/Assembly-CSharp.dll" \
  "$SRC/WreckCleanupMod.cs"

if [ "$1" == "deploy" ]; then
  cp "$OUT/WreckCleanup.dll" "$BIN/Mods/WreckCleanup.dll"
  echo "deployed -> $BIN/Mods/WreckCleanup.dll"
fi
