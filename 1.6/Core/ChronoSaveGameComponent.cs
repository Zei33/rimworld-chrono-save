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
            
            // Skip if chronosave is disabled or saving is temporarily disabled
            if (!Settings.ChronoSaveEnabled || GameDataSaveLoader.SavingIsTemporarilyDisabled)
            {
                return;
            }
            
            // Check if enough real time has passed
            float currentRealTime = Time.realtimeSinceStartup;
            float timeSinceLastSave = currentRealTime - lastSaveRealTime;
            float intervalInSeconds = Settings.SaveIntervalMinutes * 60f;
            
            if (timeSinceLastSave >= intervalInSeconds)
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
                string saveName = GetNextChronoSaveName();
                
                // Queue the save operation as a long event to prevent UI freezing
                LongEventHandler.QueueLongEvent(() =>
                {
                    GameDataSaveLoader.SaveGame(saveName);
                    Messages.Message("ChronoSave_SavedMessage".Translate(saveName), MessageTypeDefOf.SilentInput);
                }, "ChronoSave_SavingMessage", false, null);
                
                // Update tracking variables
                lastSaveRealTime = Time.realtimeSinceStartup;
                currentSaveIndex++;
                if (currentSaveIndex > Settings.NumberOfSaves)
                {
                    currentSaveIndex = 1;
                }
                
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
            // Find the next available index, handling cases where settings changed
            for (int i = 0; i < Settings.NumberOfSaves; i++)
            {
                string testName = $"Chronosave-{currentSaveIndex}";
                
                // If we're within our configured range, use this index
                if (currentSaveIndex <= Settings.NumberOfSaves)
                {
                    return testName;
                }
                
                // Otherwise, wrap around
                currentSaveIndex = 1;
            }
            
            return $"Chronosave-{currentSaveIndex}";
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
                if (currentSaveIndex < 1 || currentSaveIndex > 25)
                {
                    currentSaveIndex = 1;
                }
            }
        }
    }
}
