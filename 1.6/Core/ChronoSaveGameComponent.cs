using System;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ChronoSave.Core
{
    /// <summary>
    /// Game component that tracks real-world time and triggers chronosaves at configured intervals.
    /// </summary>
    public class ChronoSaveGameComponent : GameComponent
    {
        /// <summary>
        /// The last real time when a chronosave was performed.
        /// </summary>
        private float lastSaveRealTime;
        
        /// <summary>
        /// Current chronosave index (1 to NumberOfSaves).
        /// </summary>
        private int currentSaveIndex = 1;
        
        /// <summary>
        /// Whether we've performed the initial time sync after loading.
        /// </summary>
        private bool hasInitialized = false;
        
        /// <summary>
        /// Gets the mod settings instance.
        /// </summary>
        private ChronoSaveSettings Settings => ChronoSaveMod.Settings;
        
        /// <summary>
        /// Initializes a new instance of the ChronoSaveGameComponent.
        /// </summary>
        public ChronoSaveGameComponent(Game game) : base()
        {
            // Constructor required by GameComponent system
        }
        
        /// <summary>
        /// Called when the component is fully initialized.
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            
            // Set initial save time to current time to prevent immediate save
            if (!hasInitialized)
            {
                lastSaveRealTime = Time.realtimeSinceStartup;
                hasInitialized = true;
                Log.Message($"[Chrono Save] Initialized. Next save in {Settings.SaveIntervalMinutes} minutes.");
            }
        }
        
        /// <summary>
        /// Called when a new game is started.
        /// </summary>
        public override void StartedNewGame()
        {
            base.StartedNewGame();
            ResetSaveTimer();
        }
        
        /// <summary>
        /// Called when a game is loaded.
        /// </summary>
        public override void LoadedGame()
        {
            base.LoadedGame();
            ResetSaveTimer();
        }
        
        /// <summary>
        /// Called every frame while the game is running.
        /// </summary>
        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            
            // Only save once the game is actually being played. GameComponentUpdate also runs
            // during the pre-game screens: Root_Entry.Update calls Current.Game.UpdateEntry,
            // which is nothing but GameComponentUtility.GameComponentUpdate, and Current.Game
            // exists from Page_SelectScenario onwards. Find.World, Find.WorldInterface and
            // Current.Game are all non-null from the landing-site page, so they do not separate
            // the two states. Saving there throws inside Verse.Game.ExposeData, whose final
            // statement is an unguarded Find.CameraDriver.Expose(), and CameraDriver is null in
            // the entry scene. Scribe_Deep catches that and does not rethrow, so a truncated save
            // with an empty <maps /> and no camera is committed to disk. ProgramState is the gate
            // vanilla uses for its own save menu item.
            if (Current.ProgramState != ProgramState.Playing)
            {
                return;
            }

            // Skip if not fully initialized (prevents null reference during game startup)
            if (Find.World == null || Find.WorldInterface == null || Current.Game == null)
            {
                return;
            }
            
            // Skip if chronosave is disabled or saving is temporarily disabled
            if (!Settings.ChronoSaveEnabled || GameDataSaveLoader.SavingIsTemporarilyDisabled)
            {
                return;
            }
            
            // Check if enough real time has passed
            if (ChronoSaveSchedule.IsDue(lastSaveRealTime, Time.realtimeSinceStartup, Settings.SaveIntervalMinutes))
            {
                PerformChronoSave();
            }
        }
        
        /// <summary>
        /// Performs a chronosave operation.
        /// </summary>
        private void PerformChronoSave()
        {
            try
            {
                // Double-check game state before attempting save
                if (Current.Game == null)
                {
                    Log.Warning("[Chrono Save] Cannot save: Current.Game is null");
                    return;
                }
                
                if (Current.ProgramState != ProgramState.Playing)
                {
                    Log.Warning("[Chrono Save] Cannot save: not in play. ProgramState is " + Current.ProgramState);
                    return;
                }
                
                string saveName = GetNextChronoSaveName();
                
                // Queue the save operation as a long event to prevent UI freezing
                LongEventHandler.QueueLongEvent(() =>
                {
                    // Final safety check inside the queued operation. QueueLongEvent defers this,
                    // so the player can have returned to the main menu in between, which puts
                    // ProgramState back to Entry and nulls CameraDriver.
                    if (Current.Game == null)
                    {
                        Log.Warning("[Chrono Save] Save aborted: Current.Game became null during queue");
                        return;
                    }
                    
                    if (Current.ProgramState != ProgramState.Playing)
                    {
                        Log.Warning("[Chrono Save] Save aborted: left play during queue. ProgramState is " + Current.ProgramState);
                        return;
                    }
                    
                    GameDataSaveLoader.SaveGame(saveName);
                    Messages.Message("ChronoSave_SavedMessage".Translate(saveName), MessageTypeDefOf.SilentInput);
                }, "ChronoSave_SavingMessage", false, null);
                
                // Update tracking variables
                lastSaveRealTime = Time.realtimeSinceStartup;
                currentSaveIndex = ChronoSaveSchedule.AdvanceSlot(currentSaveIndex, Settings.NumberOfSaves);
                
                Log.Message($"[Chrono Save] Saved game as {saveName}. Next save in {Settings.SaveIntervalMinutes} minutes.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Chrono Save] Failed to perform chronosave: {ex}");
            }
        }
        
        /// <summary>
        /// Gets the name for the next chronosave file.
        /// </summary>
        private string GetNextChronoSaveName()
        {
            // Lowering NumberOfSaves can leave the stored slot above the new limit, so bring it back
            // into range first. The loop this replaced did the same thing the long way round: it
            // returned on its first iteration in every case bar that one.
            currentSaveIndex = ChronoSaveSchedule.SlotInRange(currentSaveIndex, Settings.NumberOfSaves);
            return ChronoSaveSchedule.SaveNameForSlot(currentSaveIndex);
        }
        
        /// <summary>
        /// Resets the save timer to prevent immediate saves after loading.
        /// </summary>
        private void ResetSaveTimer()
        {
            lastSaveRealTime = Time.realtimeSinceStartup;
            hasInitialized = true;
        }
        
        /// <summary>
        /// Saves and loads component data.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Values.Look(ref currentSaveIndex, "currentSaveIndex", 1);
            Scribe_Values.Look(ref hasInitialized, "hasInitialized", false);
            
            // Don't save lastSaveRealTime as it should reset on load
            
            // Validate loaded values
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                currentSaveIndex = ChronoSaveSchedule.SanitiseLoadedSlot(currentSaveIndex);
            }
        }
    }
}
