#!/bin/bash
set -e

export FrameworkPathOverride=/opt/homebrew/opt/mono/lib/mono/4.7.2-api

if [ -z "${RimWorldDir}" ]; then
  echo "RimWorldDir is not set. Export it to your RimWorld install before building." >&2
  exit 1
fi

rm -Rf release
dotnet build rimworld-chrono-save.sln -c Release
mkdir -p release

cp -r About release/About
cp -r 1.6 release

# Ship only our own assembly. Anything else in here is a build artefact or a
# game assembly that RimWorld already has loaded, and shipping it breaks mods.
find release/1.6/Assemblies/net472 -type f ! -name 'ChronoSave.dll' -delete
# No vendored libraries: this mod has no Harmony dependency and no Libraries folder.

rm -Rf "${RimWorldDir}/Mods/ChronoSave"
cp -r release "${RimWorldDir}/Mods/ChronoSave"
rm -Rf release
