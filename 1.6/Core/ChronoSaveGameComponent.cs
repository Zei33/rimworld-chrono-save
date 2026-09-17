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
        /// True between queueing a chronosave and that queued event finishing.
        /// </summary>
        /// <remarks>
        /// Load bearing, and not obvious. A queued long event does not stop the game updating:
        /// <c>LongEventHandler.ShouldWaitForEvent</c> returns false while the current event uses the
        /// standard window, and this mod's event does, being synchronous with a plain
        /// <c>Action</c>. So <c>Root_Play.Update</c> keeps calling <c>UpdatePlay</c> and this
        /// component keeps ticking in the frames between queueing a save and the save running. The
        /// interval condition is still true in those frames, so without this latch a second save is
        /// queued one frame after the first.
        ///
        /// Deliberately not scribed. <c>ExposeData</c> runs inside the queued event, so a scribed
        /// copy would be written as true and every loaded game would start with chronosaving
        /// switched off.
        /// </remarks>
        private bool savePending;

        /// <summary>
        /// Length of the last chronosave this session that verified, or zero when none has.
        /// </summary>
        /// <remarks>Deliberately not scribed: it is a within-session baseline, not colony state.</remarks>
        private long lastGoodSaveBytes;

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
            
            // A chronosave is queued and has not run yet. See the remarks on savePending: this
            // method keeps firing while the event waits, and the interval condition is still true.
            //
            // Checked before the settings and SavingIsTemporarilyDisabled guards deliberately, so
            // the stale-latch watchdog below gets a chance to run in every state where this
            // component is alive rather than only in the ones where a save would be allowed.
            if (savePending)
            {
                // GenScene.GoToMainMenu calls LongEventHandler.ClearQueuedEvents before disposing
                // the game, so a queued chronosave can be dropped without its finally ever running.
                // No event queued and none running means ours is gone and the latch is stale.
                if (!LongEventHandler.AnyEventNowOrWaiting)
                {
                    savePending = false;
                    Log.Warning("[Chrono Save] A queued chronosave was discarded before it ran. Rescheduling.");
                }

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

                // Captured so the closure judges the attempt against the state it was queued for.
                // Identity, not just non-null: loading another colony in the window between
                // queueing and executing replaces Current.Game, and a null check does not see that.
                int queuedSlot = currentSaveIndex;
                Game queuedGame = Current.Game;

                // Queue the save operation as a long event to prevent UI freezing. Nothing after
                // this point reports anything: the message, the log line, the timer and the slot
                // all live in ApplyOutcome, which only the closure reaches, and only once the write
                // has been attempted and measured.
                LongEventHandler.QueueLongEvent(() =>
                {
                    try
                    {
                        // Final safety checks inside the queued operation. QueueLongEvent defers
                        // this, so the player can have returned to the main menu or loaded another
                        // colony in between. Each of these is also checked before queueing; getting
                        // here means the state changed inside that window.
                        if (!ReferenceEquals(Current.Game, queuedGame))
                        {
                            ApplyOutcome(ChronoSaveOutcome.Aborted, queuedSlot, saveName,
                                "the game was replaced during the queue");
                            return;
                        }

                        if (Current.ProgramState != ProgramState.Playing)
                        {
                            ApplyOutcome(ChronoSaveOutcome.Aborted, queuedSlot, saveName,
                                "left play during the queue, ProgramState is " + Current.ProgramState);
                            return;
                        }

                        if (GameDataSaveLoader.SavingIsTemporarilyDisabled)
                        {
                            ApplyOutcome(ChronoSaveOutcome.Aborted, queuedSlot, saveName,
                                "saving became temporarily disabled during the queue");
                            return;
                        }

                        // Returns void and swallows every exception into Log.Error, so nothing after
                        // this can learn from a thrown exception whether it worked.
                        GameDataSaveLoader.SaveGame(saveName);

                        long writtenBytes = ChronoSaveFiles.MeasureSaveFile(GenFilePaths.FilePathForSavedGame(saveName));
                        bool plausible = ChronoSaveSchedule.IsPlausibleSaveSize(writtenBytes, lastGoodSaveBytes);
                        string detail = $"{writtenBytes} bytes written, previous good save was {lastGoodSaveBytes} bytes";

                        if (plausible)
                        {
                            lastGoodSaveBytes = writtenBytes;
                            ApplyOutcome(ChronoSaveOutcome.Succeeded, queuedSlot, saveName, detail);
                            return;
                        }

                        ApplyOutcome(ChronoSaveOutcome.Failed, queuedSlot, saveName, detail);
                    }
                    catch (Exception ex)
                    {
                        // SaveGame itself cannot reach here. The path lookup, the measurement and
                        // the message can.
                        ApplyOutcome(ChronoSaveOutcome.Failed, queuedSlot, saveName, "chronosave threw: " + ex);
                    }
                    finally
                    {
                        // Load bearing. Without it a throw is handled by LongEventHandler, which
                        // knows nothing about this latch, and chronosaving stops silently for the
                        // rest of the session.
                        savePending = false;
                    }
                }, "ChronoSave_SavingMessage", false, null);

                savePending = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Chrono Save] Failed to queue a chronosave: {ex}");
            }
        }

        /// <summary>
        /// Applies the timer, slot, message and log consequences of a finished chronosave attempt.
        /// </summary>
        /// <param name="outcome">What the attempt actually did.</param>
        /// <param name="slot">The slot the attempt was made against.</param>
        /// <param name="saveName">The filename the attempt used.</param>
        /// <param name="detail">Detail for the log only; never shown to the player.</param>
        /// <remarks>
        /// The only place any of those four things happen. Every outcome writes
        /// <c>lastSaveRealTime</c>, including the ones that failed, because the latch clears as this
        /// returns and the frame update runs again immediately: leaving the timer alone would make
        /// the whole cycle repeat every few frames.
        /// </remarks>
        private void ApplyOutcome(ChronoSaveOutcome outcome, int slot, string saveName, string detail)
        {
            ChronoSaveResolution resolution = ChronoSaveSchedule.Resolve(
                outcome,
                Time.realtimeSinceStartup,
                Settings.SaveIntervalMinutes);

            lastSaveRealTime = resolution.LastSaveRealTime;
            currentSaveIndex = ChronoSaveSchedule.NextSlot(outcome, slot, Settings.NumberOfSaves);

            if (resolution.ShowSuccessMessage)
            {
                // historical: false. The default routes this to Find.Archive.Add, which culls
                // non-pinned entries above MaxNonPinnedArchivables oldest first, so twelve of these
                // an hour evicts the player's real gameplay events from the history tab.
                Messages.Message("ChronoSave_SavedMessage".Translate(saveName), MessageTypeDefOf.SilentInput, historical: false);

                if (Prefs.DevMode)
                {
                    Log.Message($"[Chrono Save] Saved {saveName}. {detail}. Next save in {Settings.SaveIntervalMinutes} minutes.");
                }

                return;
            }

            if (resolution.ShowFailureMessage)
            {
                Messages.Message("ChronoSave_SaveFailedMessage".Translate(saveName), MessageTypeDefOf.NegativeEvent, historical: false);
                Log.Error($"[Chrono Save] {saveName} did not verify: {detail}. The slot is kept and will be rewritten.");
                return;
            }

            Log.Warning($"[Chrono Save] Chronosave called off before writing: {detail}. The slot is unchanged.");
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

            // A different colony is in play, so the size baseline means nothing, and no chronosave
            // queued against the previous one can still be ours.
            savePending = false;
            lastGoodSaveBytes = 0L;
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
