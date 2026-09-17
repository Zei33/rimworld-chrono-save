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
        /// The deferral reason last written to the log, so it is written once rather than per frame.
        /// </summary>
        /// <remarks>Not scribed.</remarks>
        private ChronoSaveBlocker lastLoggedBlocker = ChronoSaveBlocker.None;

        /// <summary>
        /// Whether the missing-settings error has already been logged this session.
        /// </summary>
        /// <remarks>Not scribed. The frame update would otherwise repeat it sixty times a second.</remarks>
        private bool missingSettingsReported;

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

                if (Settings != null)
                {
                    Log.Message($"[Chrono Save] Initialized. Next save in {Settings.SaveIntervalMinutes} minutes.");
                }
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
            
            if (Settings == null)
            {
                // Only reachable if the Mod constructor threw, which LoadedModManager logs and then
                // carries on from, leaving the static null. Vanilla still creates this component, so
                // without the guard every frame after that produces another red error. Logged once.
                if (!missingSettingsReported)
                {
                    missingSettingsReported = true;
                    Log.Error("[Chrono Save] Settings were never created, so chronosaving is off for this session. The mod's constructor must have failed earlier in the log.");
                }

                return;
            }

            // Every read the decision needs, taken once, then handed to a pure function. The
            // readings are untestable and the decision is not, which matters because the decision
            // is the half that has been wrong.
            ChronoSaveConditions conditions = ReadConditions();

            if (rebindCheckPending && conditions.Playing && conditions.WorldReady)
            {
                // Ahead of the blocker check, so it still runs for the Commitment colonies the
                // CommitmentMode blocker is about to stop saving. Those are the ones it is for.
                rebindCheckPending = false;
                WarnIfCommitmentSaveIsBoundToASlot();
            }

            ChronoSaveBlocker blocker = ChronoSaveSchedule.FirstBlocker(conditions);
            if (blocker != ChronoSaveBlocker.None)
            {
                // Deferral, not cancellation. lastSaveRealTime is untouched, so the interval
                // condition stays true and the chronosave goes out on the first frame the state
                // clears. That is what the 4 Aug 2026 report asked for, and it is why nothing on
                // this path may write to lastSaveRealTime.
                if (Prefs.DevMode && ChronoSaveSchedule.ShouldLogBlocker(blocker, lastLoggedBlocker))
                {
                    Log.Message("[Chrono Save] Chronosave deferred: " + blocker);
                }

                lastLoggedBlocker = blocker;
                return;
            }

            lastLoggedBlocker = ChronoSaveBlocker.None;

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
                // No re-checking of Current.Game or ProgramState here. This method is only reached
                // when FirstBlocker returned None, which has already established both, and
                // GameComponentUtility.GameComponentUpdate opens with Current.Game.components, so a
                // null game could not have got this far in the first place.

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

                        // The whole condition set again, not a subset. A frame or two has passed
                        // since this was queued, so a dialog, a targeter or a route planner can have
                        // opened in between, and loading another colony can have made this a
                        // Commitment one, which the identity check above does not cover because it
                        // only rules out the Game being replaced.
                        //
                        // IgnoringOwnLongEvent drops exactly one reading, the long event, which is
                        // necessarily true here because this closure is that event.
                        ChronoSaveBlocker blocker =
                            ChronoSaveSchedule.FirstBlocker(ReadConditions().IgnoringOwnLongEvent());

                        if (blocker != ChronoSaveBlocker.None)
                        {
                            ApplyOutcome(ChronoSaveOutcome.Aborted, null, "blocked after queueing by " + blocker);
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
                }, "ChronoSave_SavingMessage", false, null);
            }
            catch (Exception ex)
            {
                Log.Error($"[Chrono Save] Failed to queue a chronosave: {ex}");
            }
        }

        /// <summary>
        /// Takes this frame's readings of the game state a chronosave depends on.
        /// </summary>
        /// <returns>The readings, for <see cref="ChronoSaveSchedule.FirstBlocker"/> to judge.</returns>
        /// <remarks>
        /// The order is load bearing and the early returns are not an optimisation. Each reading is
        /// only legal once the one above it holds.
        ///
        /// <c>Current.ProgramState</c> has to come first, because this method also runs on the
        /// pre-game screens: <c>Root_Entry.Update</c> calls <c>Current.Game.UpdateEntry</c>, whose
        /// body is nothing but <c>GameComponentUtility.GameComponentUpdate</c>, and
        /// <c>Current.Game</c> exists from <c>Page_SelectScenario</c> onwards. <c>Find.World</c>,
        /// <c>Find.WorldInterface</c> and <c>Current.Game</c> are all non-null from the landing-site
        /// page, so they do not separate the two states. <c>ProgramState</c> is the gate vanilla uses
        /// for its own save menu item.
        ///
        /// Then <c>Find.Targeter</c> is <c>((UIRoot_Play)Find.UIRoot).mapUI.targeter</c> and throws
        /// on the entry screen where <c>Find.UIRoot</c> is a <c>UIRoot_Entry</c>, and
        /// <c>GameDataSaveLoader.SavingIsTemporarilyDisabled</c> dereferences <c>Find.TilePicker</c>,
        /// which chains through <c>Find.World</c>.
        /// </remarks>
        private ChronoSaveConditions ReadConditions()
        {
            ChronoSaveConditions conditions = ChronoSaveConditions.AllClear();

            conditions.Playing = Current.ProgramState == ProgramState.Playing && Current.Game != null;
            if (!conditions.Playing)
            {
                return conditions;
            }

            conditions.WorldReady = Find.World != null && Find.WorldInterface != null;
            if (!conditions.WorldReady)
            {
                return conditions;
            }

            // A game with no info cannot be asked whether it is a Commitment colony, so it is
            // treated as one and nothing is written. That state is already broken anyway: vanilla's
            // own Autosaver.AutosaveIntervalDays throws on the first tick without it.
            GameInfo info = Current.Game.Info;
            conditions.CommitmentMode = info == null || info.permadeathMode;

            conditions.Enabled = Settings.ChronoSaveEnabled;
            conditions.ScribeActive = Scribe.mode != LoadSaveMode.Inactive;
            conditions.LongEventPending = LongEventHandler.AnyEventNowOrWaiting;
            conditions.VanillaSavingDisabled = GameDataSaveLoader.SavingIsTemporarilyDisabled;
            conditions.MapTargeterActive = Find.Targeter.IsTargeting;
            conditions.WorldTargeterActive = Find.WorldTargeter.IsTargeting;
            conditions.RoutePlannerActive = Find.WorldRoutePlanner.Active;

            WindowStack windows = Find.WindowStack;
            conditions.ModalWindowOpen = windows != null && windows.AnyWindowAbsorbingAllInput;
            conditions.FloatMenuOpen = windows != null && windows.IsOpen<FloatMenu>();

            return conditions;
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
        private void WarnIfCommitmentSaveIsBoundToASlot()
        {
            GameInfo info = Current.Game != null ? Current.Game.Info : null;
            if (info == null)
            {
                return;
            }

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
            lastGoodSaveBytes = 0L;
            pendingRetryName = null;
            lastLoggedBlocker = ChronoSaveBlocker.None;
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
