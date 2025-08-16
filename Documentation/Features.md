# Chrono Save Features

## Core Features

### Real-Time Based Autosaving
- Creates automatic saves based on real-world time passage
- Works independently of game speed and pause state
- Default interval: 5 minutes real time
- Configurable interval: 1-60 minutes

### Rotating Save System
- Maintains a configurable number of save files (default: 10)
- Save files named "Chronosave-1" through "Chronosave-X"
- Automatically overwrites oldest saves when limit is reached
- Preserves recent save history without cluttering save folder

### Mod Settings
Access through Options > Mod Settings > Chrono Save

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
- Won't save during certain UI operations

#### Integration
- Fully compatible with vanilla autosave system
- Chronosaves appear in standard load game menu
- Can be loaded like any other save file
- Separate from permadeath mode restrictions

## Technical Features

### Performance Optimized
- Minimal performance impact
- Uses frame updates, not game ticks
- Asynchronous save operations
- No impact when disabled

### Compatibility
- Works with other mods
- Minimal Harmony patching
- No save game corruption risk
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
| Permadeath override | No | No |

## Future Considerations

The mod is designed to be extended with potential features like:
- Save compression options
- Cloud backup integration
- Save file management tools
- Custom save name patterns
- Save event triggers

However, the current implementation focuses on reliability and simplicity.