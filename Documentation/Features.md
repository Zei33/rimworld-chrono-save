# Chrono Save Features

## Core Features

### Real-Time Based Autosaving
- Creates automatic saves based on real-world time passage
- Works independently of game speed and pause state
- Default interval: 5 minutes real time
- Configurable interval: 1-60 minutes

### Rotating Save System
- Maintains a configurable number of save files (default: 10)
- Once a colony is named, its saves are "Chronosave-YourColony-1" through "Chronosave-YourColony-X",
  so two colonies never overwrite each other
- Before a colony is named, which in RimWorld takes at least 4.3 in-game days, its saves go to a
  shared set named "Chronosave-1" through "Chronosave-X". Existing chronosaves from earlier versions
  are that shared set, so nothing already on disk is stranded
- The slot to write is read from the Saves folder each time: the first unused one, otherwise the one
  written longest ago. This is the same rule RimWorld's own autosaver uses
- Preserves recent save history without cluttering save folder

### Mod Settings
Access through Options, then mod options, then Chrono Save

#### Enable/Disable Toggle
- Turn the chronosave system on or off
- Useful for temporarily disabling during specific gameplay situations

#### Save Interval Configuration
- Set how often saves occur (in real minutes)
- Text input field with validation
- Range: 1-60 minutes
- Changes take effect immediately

#### Number of Saves
- Configure how many chronosave files to maintain
- Slider interface: 1-25 saves
- Higher numbers = more save history but more disk space

### User Interface

#### Settings Window
- Clean, intuitive settings interface
- Tooltips explain each option
- Info section describes mod functionality
- Real-time validation of input values

#### In-Game Notifications
- Silent notification when chronosave completes
- Non-intrusive message in notification area
- Shows which save file was created

### Save Behavior

#### Smart Timing
- Resets timer when game is loaded/started
- Prevents immediate save after loading
- Respects game's temporary save disable states
- Holds off while you are in the middle of something: picking a target on the map, picking a shuttle
  or transport pod destination on the world map, planning a caravan route, or with a dialog or a
  right-click menu open
- A held-off save is not skipped. It goes out on the first moment the way is clear, rather than
  waiting another full interval

#### Integration
- Fully compatible with vanilla autosave system
- Chronosaves appear in standard load game menu
- Can be loaded like any other save file
- Does nothing while a Commitment mode colony is loaded. That mode is built around a single save
  file, and any extra copy is a way to roll back, so no chronosaves are written there

#### Commitment mode
- No chronosaves are written while a colony in Commitment mode is loaded
- The mod says so once in the log when the colony loads, and in its settings window
- There is no setting to turn this off. Commitment mode has one save file by design, and every extra
  copy is a rollback point
- If an earlier version left a Commitment colony saving into a chronosave slot, the mod sends a
  letter explaining the fix: Options, then mod options, then Chrono Save, then Rename colony.
  RimWorld saves the colony under its own name again and removes the slot file, which is what it
  already does when a colony is first named
- Backup files RimWorld left behind from those saves are counted in the settings window. The mod
  never deletes them, because a file of that shape can also be RimWorld's own safety copy for a
  colony that has nothing to do with this mod

## Technical Features

### Performance Optimized
- Minimal performance impact
- Uses frame updates, not game ticks
- Saves synchronously, the same way a vanilla autosave does: the game stops for the moment the file
  is written rather than serialising a live game from another thread
- No impact when disabled

### Compatibility
- Works with other mods
- No Harmony patches at all, so no patch conflicts are possible, and no Harmony dependency
- Every chronosave is checked after it is written, and you are told if one does not look complete
- Safe to add/remove mid-game

### Localization
Full translations for:
- English
- Chinese Simplified
- French  
- German
- Japanese
- Polish
- Portuguese (Brazilian)
- Russian
- Spanish

## Use Cases

1. **Development/Testing**: Regular saves while testing mods or scenarios
2. **Streaming/Recording**: Ensure saves even during long pause discussions
3. **Multiplayer**: Consistent saves regardless of game speed changes
4. **Safety Net**: Additional save backup system for important colonies
5. **AFK Protection**: Saves continue even when game is paused

## Comparison with Vanilla Autosave

| Feature | Vanilla Autosave | Chrono Save |
|---------|-----------------|-------------|
| Trigger | In-game days | Real time |
| Works when paused | No | Yes |
| Configurable count | Yes | Yes |
| Separate file names | Yes | Yes |
| Runs in Commitment mode | Yes, overwrites the colony's single save file | No, writes nothing |

## Future Considerations

The mod is designed to be extended with potential features like:
- Save compression options
- Cloud backup integration
- Save file management tools
- Custom save name patterns
- Save event triggers

However, the current implementation focuses on reliability and simplicity.