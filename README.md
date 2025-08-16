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
- **Pause-Friendly**: Continues saving even when game is paused
- **Rotating Saves**: Maintains a set number of saves before overwriting oldest
- **Full Localization**: Supports 9 languages

## Installation

1. Subscribe on Steam Workshop or download from releases
2. Enable the mod in RimWorld's mod menu
3. No load order requirements - compatible with all mods

## Configuration

Access settings through: **Options → Mod Settings → Chrono Save**

### Settings

- **Enable Chrono Save**: Toggle the system on/off
- **Save Interval**: How often to save (1-60 minutes)
- **Number of Saves**: How many chronosave files to keep (1-25)

## How It Works

The mod tracks real-world time using Unity's time system. When the configured interval passes, it creates a save file named "Chronosave-X" where X rotates from 1 to your configured maximum.

For example, with default settings:
- Chronosave-1 (created at 0:05)
- Chronosave-2 (created at 0:10)
- ...
- Chronosave-10 (created at 0:50)
- Chronosave-1 (overwrites at 0:55)

## Compatibility

- **RimWorld Version**: 1.6
- **Multiplayer**: Compatible
- **Save Games**: Safe to add/remove mid-game
- **Other Mods**: No known conflicts

## Technical Details

- Uses Harmony for minimal game patching
- Implements a custom GameComponent for time tracking
- Saves are created using RimWorld's standard save system
- Performance impact: Negligible

## FAQ

**Q: Will this interfere with vanilla autosaves?**
A: No, Chrono Save operates independently and creates separate save files.

**Q: What happens if I disable the mod?**
A: Existing chronosaves remain playable. The mod can be safely removed.

## Support

For issues or suggestions:
- Report bugs on the GitHub issues page
- Steam Workshop discussions