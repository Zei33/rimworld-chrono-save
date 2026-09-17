# Chrono Save Architecture

## Overview

Chrono Save is a RimWorld mod that implements a secondary autosave system based on real-world time rather than in-game time. This ensures regular saves occur even when the game is paused or running at different speeds.

## Core Components

### 1. ChronoSaveMod (Entry Point)
- **Location**: `1.6/ModEntry.cs`
- **Purpose**: Holds the settings and draws their window
- **Responsibilities**:
  - Load and manage mod settings
  - Provide settings UI integration
  - Settle the interval field's edit buffer when the window closes

### 2. ChronoSaveGameComponent
- **Location**: `1.6/Core/ChronoSaveGameComponent.cs`
- **Purpose**: Core game component that tracks real time and triggers saves
- **Key Features**:
  - Tracks elapsed real time using Unity's `Time.realtimeSinceStartup`
  - Takes the game-state readings each frame and defers when any of them blocks
  - Queues save operations using `LongEventHandler`
  - Chooses the filename, writes, measures what landed, and only then reports
  - Resets timer on game load/start to prevent immediate saves

### 3. ChronoSaveSettings
- **Location**: `1.6/Core/ChronoSaveSettings.cs`
- **Purpose**: Manages mod configuration and settings UI
- **Configurable Options**:
  - Enable/disable chronosave system
  - Save interval in minutes (1-60)
  - Number of save files to maintain (1-25)

### 4. The pure layer
- **Locations**: `1.6/Core/ChronoSaveSchedule.cs`, `ChronoSaveConditions.cs`, `ChronoSaveOutcome.cs`, `SaveFileStamp.cs`
- **Purpose**: Every decision that can be stated over plain values, kept apart from the game reads that produce them
- **Why**: `Find.*`, `Current.Game` and `Time.realtimeSinceStartup` cannot be reached outside a running game, so the component takes the readings and these decide. That split is what makes the decisions testable, and the decisions are the half that has historically been wrong.

### 5. The filesystem layer
- **Locations**: `1.6/Core/ChronoSaveFiles.cs`, `StrandedBackups.cs`
- **Purpose**: List the saves folder, measure a written file, count leftover backup copies
- **Why separate**: these take a plain directory path rather than calling `GenFilePaths`, so the method that runs in game is the same one the tests exercise against a temporary directory

## Technical Design

### Real-Time Tracking
The mod uses Unity's `Time.realtimeSinceStartup` to track real-world time passage. This value continues to increment regardless of game pause state or speed settings.

### Save Timing Logic
1. `GameComponentUpdate()` is called every frame
2. Reads the game state and asks whether anything blocks a save right now
3. If something does, returns without touching the timer, so the save is deferred rather than skipped
4. Otherwise, if the elapsed time since the last save is at least the configured interval, queues one
5. The queued work chooses the filename, writes, measures the result, and updates the timer

### Save File Management
- Once a colony has been named, its saves are `Chronosave-<ColonyName>-1` upwards, so two colonies never share files
- Before a colony is named, which takes at least 4.3 in-game days, its saves go to a shared set named `Chronosave-1` upwards. Chronosaves from earlier versions of the mod are that shared set
- The slot is read from the Saves folder each time: the first unused one, otherwise the one written longest ago. This is the rule RimWorld's own autosaver uses
- No rotation state is stored in the save file. It used to be, and loading an older save then rewound the rotation and overwrote newer chronosaves

### Integration Points
- **Harmony**: none. The mod has no patches and no Harmony dependency
- **Game Systems**: Uses standard `GameDataSaveLoader.SaveGame()` API
- **Component creation**: `Verse.Game.FillComponents` constructs every non-abstract `GameComponent` subclass it finds, including those in mod assemblies, so nothing has to inject anything
- **UI Integration**: Standard ModSettings system for configuration

## Performance Considerations

1. **Frame-based Updates**: Checking occurs every frame but is lightweight: a handful of field reads and a time comparison
2. **Synchronous Saves**: `LongEventHandler.QueueLongEvent` is called with `doAsynchronously: false`, so the game stops while the file is written, exactly as it does for a vanilla autosave. This is deliberate; serialising a live game from another thread is not safe
3. **No Tick Usage**: Avoids game tick system to work when paused

## Compatibility

The mod is designed for maximum compatibility:
- No Harmony patches at all, so no patch conflicts are possible
- No modification of existing game behaviour
- Independent of vanilla autosave system
- Save files are standard RimWorld saves
- Writes nothing at all while a Commitment mode colony is loaded

## Error Handling

- Try-catch blocks around critical operations
- Logging for debugging (`[Chrono Save]` prefix)
- Validation of settings values on load
- The written file is measured after every save, because `GameDataSaveLoader.SaveGame` returns `void` and catches every exception into the log. A save that fails part way through is still committed as a well formed but truncated document, so the absence of an exception is not evidence of success
