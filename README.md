# Chrono Save

A RimWorld mod that adds real-time based autosaving to complement the game's standard autosave system.

![RimWorld Version](https://img.shields.io/badge/RimWorld-1.6-brightgreen.svg)
![License](https://img.shields.io/badge/License-GPL%20v3-blue.svg)

## Overview

Chrono Save creates automatic saves based on real-world time rather than in-game time. This ensures your colony is regularly saved even when the game is paused, running at different speeds, or you're away from your computer.

## Features

- **Real-Time Autosaves**: Saves every X minutes of real time (default: 5 minutes)
- **Configurable Settings**: Adjust save interval and number of saves to keep
- **Independent System**: Works alongside vanilla autosaves without interference
- **Pause-Friendly**: Continues saving even when game is paused, but holds off while you are mid-interaction
- **Rotating Saves**: Keeps a set number of saves per colony, reusing the oldest slot first
- **Full Localization**: Supports 9 languages

## Installation

1. Subscribe on Steam Workshop or download from releases
2. Enable the mod in RimWorld's mod menu
3. No load order requirements - compatible with all mods

## Configuration

Access settings through **Options**, then **mod options**, then **Chrono Save**.

### Settings

- **Enable Chrono Save**: Toggle the system on/off
- **Save Interval**: How often to save (1-60 minutes)
- **Number of Saves**: How many chronosave files to keep (1-25)

## How It Works

The mod tracks real-world time using Unity's time system. When the configured interval passes, it looks at your Saves folder and writes to the first unused slot, or if they are all in use, the one written longest ago. That is the same rule RimWorld's own autosaver follows.

Each colony you have named keeps its own set of files, named after it. Colonies you have not named yet share one set, which is also where chronosaves from earlier versions of the mod live, so nothing already on disk is stranded.

For example, with default settings and a colony named Crimson Fleet:
- Chronosave-Crimson Fleet-1 (created at 0:05)
- Chronosave-Crimson Fleet-2 (created at 0:10)
- ...
- Chronosave-Crimson Fleet-10 (created at 0:50)
- Chronosave-Crimson Fleet-1 (the oldest, reused at 0:55)

## Compatibility

- **RimWorld Version**: 1.6
- **Multiplayer**: Compatible
- **Save Games**: Safe to add/remove mid-game
- **Other Mods**: No known conflicts, and no Harmony patches to conflict with
- **Commitment mode**: No chronosaves are written while a Commitment colony is loaded

## Technical Details

- No Harmony patches and no Harmony dependency
- Implements a custom GameComponent for time tracking
- Saves are created using RimWorld's standard save system, synchronously, the same way a vanilla
  autosave is
- Every chronosave is checked after it is written, and you are told if one does not look complete
- Performance impact: Negligible

## FAQ

**Q: Will this interfere with vanilla autosaves?**
A: No, Chrono Save operates independently and creates separate save files.

**Q: What happens if I disable the mod?**
A: Existing chronosaves remain playable. The mod can be safely removed.

**Q: Does it work in Commitment mode?**
A: No, deliberately. Commitment mode is built around a colony having one save file, and every extra
copy is a way to roll back. There is no setting to change this.

## Support

For issues or suggestions:
- Report bugs on the GitHub issues page
- Steam Workshop discussions