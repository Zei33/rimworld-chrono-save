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

# Stage 1.6 by naming what ships, rather than copying it wholesale. `cp -r 1.6 release`
# put ModEntry.cs and the whole Core/ source tree into every subscriber's mod folder,
# and the game reads none of it. An allow-list is also the shape that stays correct
# when a folder is added: a new source folder is simply not staged, where a deny-list
# would ship it until somebody noticed.
mkdir -p release/1.6/Assemblies/net472

# Only our own assembly. Verse.ModAssemblyHandler.ReloadAll loads every .dll at any
# depth, filtered on extension alone, so a stray one is not inert: it binds by simple
# name, first loader wins process-wide, and a failed load abandons the rest of this
# mod's DLLs. This mod vendors no libraries, having no Harmony dependency since 1.1.0.
cp 1.6/Assemblies/net472/ChronoSave.dll release/1.6/Assemblies/net472/

cp -r 1.6/Languages release/1.6/Languages

# Keep only what the game loads, in case a developer README is ever added here as it
# was in the other two repos.
find release/1.6/Languages -type f ! -name '*.xml' -delete

rm -Rf "${RimWorldDir}/Mods/ChronoSave"
cp -r release "${RimWorldDir}/Mods/ChronoSave"
rm -Rf release
