using System;
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
        /// The chronosave name a failed attempt should be retried into, or <c>null</c>.
        /// </summary>
        /// <remarks>
        /// Deliberately not scribed. It exists so a slot that has already been spoiled is rewritten
        /// rather than abandoned: choosing afresh would skip it, because the ruined file is now the
        /// newest in the folder, and a repeating fault would then walk the ring and destroy every
        /// chronosave the player has instead of ruining the one slot over and over.
        /// </remarks>
        private string pendingRetryName;

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
        /// Whether the Commitment rebind check still has to run this session.
        /// </summary>
        /// <remarks>
        /// Not scribed, and consumed exactly once per component lifetime.
        ///
        /// The check cannot live in <c>FinalizeInit</c>, which is the obvious place and is wrong.
        /// <c>SavedGameLoaderNow.LoadGameFromSaveFileNow</c> calls
        /// <c>PermadeathModeUtility.CheckUpdatePermadeathModeUniqueNameOnGameLoad</c> after
        /// <c>LoadGame()</c> returns, which is after both <c>FinalizeInit</c> and
        /// <c>LoadedGame</c>. On the load that does the damage the XML still holds the colony's
        /// original name, so a check at <c>FinalizeInit</c> sees nothing wrong and is one session
        /// late, every time. The first frame in <c>ProgramState.Playing</c> is guaranteed to be
        /// after the rebind, because loading runs as an asynchronous long event and
        /// <c>Root_Play.Update</c> returns before <c>UpdatePlay</c> while one of those is running.
        /// </remarks>
        private bool rebindCheckPending = true;

        /// <summary>
        /// The permadeath save name already warned about, or <c>null</c>.
        /// </summary>
        /// <remarks>
        /// Scribed, unlike the other flags here, so the warning does not repeat on every load of an
        /// affected colony. Keyed on the name rather than a boolean, so a later rebind into a
        /// different slot warns again.
        /// </remarks>
        private string rebindWarningIssuedFor;

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

            // Outside the block above, deliberately. hasInitialized is scribed, so on a loaded game
            // it is already true and anything inside that branch never runs again.
            GameInfo info = Current.Game != null ? Current.Game.Info : null;
            if (info != null && info.permadeathMode)
            {
                Log.Message("[Chrono Save] This colony is in Commitment mode. Chrono Save will not write any saves while it is loaded.");
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

            GameInfo info = Current.Game.Info;
            if (info == null)
            {
                // Cannot tell whether this is a Commitment colony, so do not write. A game with no
                // info is already broken: vanilla's own Autosaver.AutosaveIntervalDays throws on the
                // first tick in that state.
                return;
            }

            if (rebindCheckPending)
            {
                // Before the gate below, so it still runs for the colonies it is about to protect.
                rebindCheckPending = false;
                WarnIfCommitmentSaveIsBoundToASlot(info);
            }

            // Commitment mode is built around there being exactly one save file, and every extra
            // copy is a way to roll back. Writing a rotating ring of up to 25 of them is the one
            // thing that mode exists to prevent, so the mod does nothing here.
            //
            // Not a setting. An opt-out labelled "save in Commitment mode" is the same defect with a
            // consent checkbox, and the mod could not honour it safely anyway while slot names are
            // shared. The player is told instead, in the log at FinalizeInit and in the settings
            // window.
            //
            // This returns before lastSaveRealTime is touched, like every other guard here, which
            // normally means the save is deferred rather than cancelled. Harmless in this case:
            // permadeathMode never changes within a game, so the deferred save never fires.
            if (info.permadeathMode)
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
                
                // Captured so the closure judges the attempt against the state it was queued for.
                // Identity, not just non-null: loading another colony in the window between
                // queueing and executing replaces Current.Game, and a null check does not see that.
                Game queuedGame = Current.Game;

                // Queue the save operation as a long event to prevent UI freezing. Nothing after
                // this point reports anything: the message, the log line and the timer all live in
                // ApplyOutcome, which only the closure reaches, and only once the write has been
                // attempted and what landed on disk measured.
                LongEventHandler.QueueLongEvent(() =>
                {
                    // Declared out here so a throw after the name was chosen still knows which file
                    // it ruined, and can therefore pin the retry to it.
                    string saveName = null;

                    try
                    {
                        // Final safety checks inside the queued operation. QueueLongEvent defers
                        // this, so the player can have returned to the main menu or loaded another
                        // colony in between. Each of these is also checked before queueing; getting
                        // here means the state changed inside that window.
                        if (!ReferenceEquals(Current.Game, queuedGame))
                        {
                            ApplyOutcome(ChronoSaveOutcome.Aborted, null,
                                "the game was replaced during the queue");
                            return;
                        }

                        if (Current.ProgramState != ProgramState.Playing)
                        {
                            ApplyOutcome(ChronoSaveOutcome.Aborted, null,
                                "left play during the queue, ProgramState is " + Current.ProgramState);
                            return;
                        }

                        if (GameDataSaveLoader.SavingIsTemporarilyDisabled)
                        {
                            ApplyOutcome(ChronoSaveOutcome.Aborted, null,
                                "saving became temporarily disabled during the queue");
                            return;
                        }

                        // The same game object can still have become a Commitment colony's, because
                        // the identity check above only rules out replacement. Cheap, and the cost
                        // of being wrong is a rollback point in the one mode that forbids them.
                        if (Current.Game.Info == null || Current.Game.Info.permadeathMode)
                        {
                            ApplyOutcome(ChronoSaveOutcome.Aborted, null,
                                "a Commitment colony is loaded");
                            return;
                        }

                        // The name is chosen here, not at queue time, and that placement is the
                        // point of the fix. It reads the faction and the saves folder from the same
                        // Game that is about to be serialised, it sees any manual save, vanilla
                        // autosave or deletion that happened while this event waited, and an
                        // attempt that aborts above has changed nothing on disk or in memory.
                        saveName = ChooseSaveName();

                        // Returns void and swallows every exception into Log.Error, so nothing after
                        // this can learn from a thrown exception whether it worked.
                        GameDataSaveLoader.SaveGame(saveName);

                        long writtenBytes = ChronoSaveFiles.MeasureSaveFile(GenFilePaths.FilePathForSavedGame(saveName));
                        bool plausible = ChronoSaveSchedule.IsPlausibleSaveSize(writtenBytes, lastGoodSaveBytes);
                        string detail = $"{writtenBytes} bytes written, previous good save was {lastGoodSaveBytes} bytes";

                        if (plausible)
                        {
                            lastGoodSaveBytes = writtenBytes;
                            ApplyOutcome(ChronoSaveOutcome.Succeeded, saveName, detail);
                            return;
                        }

                        ApplyOutcome(ChronoSaveOutcome.Failed, saveName, detail);
                    }
                    catch (Exception ex)
                    {
                        // SaveGame itself cannot reach here. The faction read, the folder listing,
                        // the path lookup, the measurement and the message can.
                        ApplyOutcome(ChronoSaveOutcome.Failed, saveName, "chronosave threw: " + ex);
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
        /// Tells the player once when RimWorld is saving a Commitment colony into a chronosave slot.
        /// </summary>
        /// <param name="info">The loaded game's info.</param>
        /// <remarks>
        /// Loading a chronosave rebinds the colony to it.
        /// <c>SavedGameLoaderNow.LoadGameFromSaveFileNow</c> unconditionally calls
        /// <c>CheckUpdatePermadeathModeUniqueNameOnGameLoad</c>, which sets
        /// <c>permadeathModeUniqueName</c> to the filename it was loaded from and says so only in a
        /// dev-log warning. Every autosave and both save-and-quit paths then write the colony into a
        /// slot this mod recycles.
        ///
        /// A letter rather than a toast or a log line. A <c>Log.Warning</c> is invisible to a normal
        /// player, a message lasts about thirteen seconds and cannot hold instructions, and a modal
        /// dialog during loading gets dismissed reflexively by someone who alt-tabbed. A letter sits
        /// on the right edge until it is read, is scribed with the letter stack so it survives the
        /// save-and-quit that is Commitment mode's only exit, and moves to the History tab when
        /// dismissed.
        /// </remarks>
        private void WarnIfCommitmentSaveIsBoundToASlot(GameInfo info)
        {
            if (!ChronoSaveSchedule.NeedsRebindWarning(info.permadeathMode, info.permadeathModeUniqueName, rebindWarningIssuedFor))
            {
                return;
            }

            rebindWarningIssuedFor = info.permadeathModeUniqueName;

            Find.LetterStack.ReceiveLetter(
                "ChronoSave_RebindLetterLabel".Translate(),
                "ChronoSave_RebindLetterText".Translate(info.permadeathModeUniqueName),
                LetterDefOf.NegativeEvent);

            Log.Warning($"[Chrono Save] This Commitment colony is saving to {info.permadeathModeUniqueName}, which is one of this mod's rotating slots.");
        }

        /// <summary>
        /// Chooses the filename for the chronosave about to be written.
        /// </summary>
        /// <returns>A save name, without a directory or an extension.</returns>
        /// <remarks>
        /// The ring is scoped to the colony, once the colony has a name. Until then it is the shared
        /// pool, whose names are exactly the flat <c>Chronosave-N</c> the mod has always written, so
        /// the files already on a subscriber's disk are adopted rather than orphaned. A colony has
        /// no name for at least its first 4.3 game days, which covers every throwaway start, so only
        /// a colony the player has committed to takes a set of files of its own.
        ///
        /// <c>Faction.HasName</c> is checked before <c>Faction.Name</c> because the getter falls
        /// back to the localised <c>def.LabelCap</c>, and filing saves under "New Arrivals" would
        /// put a colony in a different ring depending on the player's language.
        /// </remarks>
        private string ChooseSaveName()
        {
            Faction player = Faction.OfPlayerSilentFail;
            string ringKey = ChronoSaveSchedule.RingKeyFromColonyName(
                player != null && player.HasName ? player.Name : null);
            int numberOfSaves = Settings.NumberOfSaves;

            // A previous attempt spoiled this file, so rewrite it rather than leaving it behind and
            // moving on. It stops belonging to the ring when the slot count is lowered or the
            // colony gains a name, and then a slot is chosen normally.
            if (ChronoSaveSchedule.IsInRing(pendingRetryName, ringKey, numberOfSaves))
            {
                return pendingRetryName;
            }

            int slot = ChronoSaveSchedule.ChooseSlot(
                ringKey,
                numberOfSaves,
                ChronoSaveFiles.Snapshot(GenFilePaths.SavedGamesFolderPath));

            return ChronoSaveSchedule.SaveNameForSlot(ringKey, slot);
        }

        /// <summary>
        /// Applies the timer, retry, message and log consequences of a finished chronosave attempt.
        /// </summary>
        /// <param name="outcome">What the attempt actually did.</param>
        /// <param name="saveName">The filename the attempt used, or <c>null</c> if it never got one.</param>
        /// <param name="detail">Detail for the log only; never shown to the player.</param>
        /// <remarks>
        /// The only place any of those things happen. Every outcome writes <c>lastSaveRealTime</c>,
        /// including the ones that failed, because the latch clears as this returns and the frame
        /// update runs again immediately: leaving the timer alone would make the whole cycle repeat
        /// every few frames.
        /// </remarks>
        private void ApplyOutcome(ChronoSaveOutcome outcome, string saveName, string detail)
        {
            ChronoSaveResolution resolution = ChronoSaveSchedule.Resolve(
                outcome,
                Time.realtimeSinceStartup,
                Settings.SaveIntervalMinutes);

            lastSaveRealTime = resolution.LastSaveRealTime;

            if (outcome == ChronoSaveOutcome.Succeeded)
            {
                pendingRetryName = null;
            }
            else if (outcome == ChronoSaveOutcome.Failed)
            {
                pendingRetryName = saveName;
            }

            // An abort deliberately leaves pendingRetryName alone: nothing was written, so a slot
            // still owed a rewrite is still owed one.

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
                // No name means the attempt threw before it had one, so there is no file for the
                // player to go and look at and nothing useful a toast could say.
                if (saveName != null)
                {
                    Messages.Message("ChronoSave_SaveFailedMessage".Translate(saveName), MessageTypeDefOf.NegativeEvent, historical: false);
                }

                Log.Error($"[Chrono Save] Chronosave {saveName ?? "(unnamed)"} did not verify: {detail}. It will be rewritten on the next attempt.");
                return;
            }

            Log.Warning($"[Chrono Save] Chronosave called off before writing: {detail}.");
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
            pendingRetryName = null;
        }
        
        /// <summary>
        /// Saves and loads component data.
        /// </summary>
        /// <remarks>
        /// The rotation slot is deliberately not here any more, and must not come back.
        /// <c>Verse.Game.ExposeSmallComponents</c> deep-scribes <c>components</c>, so a field on
        /// this class is written into every save the game produces while the mod is active,
        /// including manual saves and vanilla autosaves. Loading any of them restored whatever slot
        /// was current when that file was written, which rewound the ring and then overwrote forward
        /// over newer chronosaves. The slot is now derived from the saves folder at the moment of
        /// writing, which is what vanilla's autosaver does.
        ///
        /// The orphaned <c>currentSaveIndex</c> node already sitting in existing saves is harmless:
        /// <c>Scribe_Values.Look</c> indexes the labels the code asks for and never enumerates
        /// children, so an unread node is simply not read, and it disappears the next time that save
        /// is written.
        ///
        /// <c>lastSaveRealTime</c> stays unscribed too, because it should reset on load.
        /// </remarks>
        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref hasInitialized, "hasInitialized", false);
            Scribe_Values.Look(ref rebindWarningIssuedFor, "rebindWarningIssuedFor");
        }
    }
}
