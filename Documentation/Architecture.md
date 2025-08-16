# Chrono Save Architecture

## Overview

Chrono Save is a RimWorld mod that implements a secondary autosave system based on real-world time rather than in-game time. This ensures regular saves occur even when the game is paused or running at different speeds.

## Core Components

### 1. ChronoSaveMod (Entry Point)
- **Location**: `1.6/ModEntry.cs`
- **Purpose**: Main mod class that initializes settings and Harmony patches
- **Responsibilities**:
  - Load and manage mod settings
  - Initialize Harmony patches
  - Provide settings UI integration

### 2. ChronoSaveGameComponent
- **Location**: `1.6/Core/ChronoSaveGameComponent.cs`
- **Purpose**: Core game component that tracks real time and triggers saves
- **Key Features**:
  - Tracks elapsed real time using Unity's `Time.realtimeSinceStartup`
  - Manages save index rotation (1 to NumberOfSaves)
  - Queues save operations using `LongEventHandler` to prevent UI freezing
  - Resets timer on game load/start to prevent immediate saves

### 3. ChronoSaveSettings
- **Location**: `1.6/Core/ChronoSaveSettings.cs`
- **Purpose**: Manages mod configuration and settings UI
- **Configurable Options**:
  - Enable/disable chronosave system
  - Save interval in minutes (1-60)
  - Number of save files to maintain (1-25)

### 4. GameComponentInjectionPatch
- **Location**: `1.6/Patches/GameComponentInjectionPatch.cs`
- **Purpose**: Harmony patch to inject ChronoSaveGameComponent into the game
- **Target**: `Game.FillComponents()` method
- **Behavior**: Adds our component after the game initializes its component list

## Technical Design

### Real-Time Tracking
The mod uses Unity's `Time.realtimeSinceStartup` to track real-world time passage. This value continues to increment regardless of game pause state or speed settings.

### Save Timing Logic
1. `GameComponentUpdate()` is called every frame
2. Calculates time elapsed since last save
3. If elapsed time >= configured interval, triggers save
4. Updates `lastSaveRealTime` to current time

### Save File Management
- Save files are named "Chronosave-X" where X is 1 to NumberOfSaves
- Index rotates: 1, 2, 3, ..., NumberOfSaves, 1, 2, ...
- Older saves are automatically overwritten when the index loops

### Integration Points
- **Harmony Patches**: Minimal patching - only injects GameComponent
- **Game Systems**: Uses standard `GameDataSaveLoader.SaveGame()` API
- **UI Integration**: Standard ModSettings system for configuration

## Performance Considerations

1. **Frame-based Updates**: Checking occurs every frame but is lightweight (simple time comparison)
2. **Async Saves**: Save operations use `LongEventHandler` to prevent blocking
3. **No Tick Usage**: Avoids game tick system to work when paused

## Compatibility

The mod is designed for maximum compatibility:
- Single Harmony patch that only adds a component
- No modification of existing game behavior
- Independent of vanilla autosave system
- Save files are standard RimWorld saves

## Error Handling

- Try-catch blocks around critical operations
- Logging for debugging (`[Chrono Save]` prefix)
- Graceful degradation if component injection fails
- Validation of settings values on load