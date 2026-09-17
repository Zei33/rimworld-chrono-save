using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ChronoSave.Core
{
    /// <summary>
    /// Mod settings for ChronoSave, allowing configuration of real-time based autosave behavior.
    /// </summary>
    public class ChronoSaveSettings : ModSettings
    {
        /// <summary>
        /// Interval between chronosaves in minutes (default: 5 minutes).
        /// </summary>
        private float saveIntervalMinutes = ChronoSaveSchedule.DefaultIntervalMinutes;
        
        /// <summary>
        /// Number of chronosave files to maintain before looping (default: 10).
        /// </summary>
        private int numberOfSaves = ChronoSaveSchedule.DefaultSlots;
        
        /// <summary>
        /// Whether the chronosave system is enabled (default: true).
        /// </summary>
        private bool chronoSaveEnabled = true;
        
        /// <summary>
        /// UI buffer for save interval input.
        /// </summary>
        private string saveIntervalBuffer = ChronoSaveSchedule.FormatIntervalBuffer(ChronoSaveSchedule.DefaultIntervalMinutes);
        
        /// <summary>
        /// Gets the save interval in minutes.
        /// </summary>
        public float SaveIntervalMinutes => saveIntervalMinutes;
        
        /// <summary>
        /// Gets the number of saves to maintain.
        /// </summary>
        public int NumberOfSaves => numberOfSaves;
        
        /// <summary>
        /// Gets whether chronosave is enabled.
        /// </summary>
        public bool ChronoSaveEnabled => chronoSaveEnabled;
        
        /// <summary>
        /// Height of the slider row, matching <c>Listing_Standard.Slider</c>.
        /// </summary>
        private const float SliderHeight = 22f;

        /// <summary>
        /// The most recent count of leftover chronosave backup files.
        /// </summary>
        /// <remarks>Not scribed: it describes the disk, not the colony.</remarks>
        private StrandedBackupReport strandedBackups = StrandedBackupReport.Empty;

        /// <summary>
        /// Real time of the last leftover-backup count.
        /// </summary>
        /// <remarks>
        /// Negative infinity so the first draw counts immediately. The window redraws every frame,
        /// so without the rate limit this would list a directory sixty times a second.
        /// </remarks>
        private float lastBackupScanRealTime = float.NegativeInfinity;

        /// <summary>
        /// Renders the mod settings window content.
        /// </summary>
        /// <param name="inRect">The rectangle area available for drawing the settings interface.</param>
        public void DoSettingsWindowContents(Rect inRect)
        {
            // The window opens from the main menu as well as from the escape menu, so there may be
            // no game at all. Find.GameInfo dereferences Current.Game without a guard, which is why
            // this reads through Current.Game and checks ProgramState first, the way vanilla's own
            // options window does.
            GameInfo info = null;
            if (Current.ProgramState == ProgramState.Playing && Current.Game != null)
            {
                info = Current.Game.Info;
            }

            bool commitmentColonyLoaded = info != null && info.permadeathMode;
            bool boundToASlot = commitmentColonyLoaded && ChronoSaveSchedule.IsRingSaveName(info.permadeathModeUniqueName);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            if (commitmentColonyLoaded)
            {
                GUI.color = ColorLibrary.RedReadable;
                listing.Label("ChronoSave_PermadeathActiveNotice".Translate());
                GUI.color = Color.white;

                if (boundToASlot)
                {
                    listing.Label("ChronoSave_RebindNotice".Translate(info.permadeathModeUniqueName));

                    // Dialog_GiveName's constructor picks a suggesting pawn with RandomElement and
                    // its draw method dereferences it, so with no free colonists anywhere vanilla
                    // logs an empty-collection error and the dialog throws. A Commitment colony with
                    // no colonists left is over anyway.
                    if (PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended.Any()
                        && listing.ButtonText("ChronoSave_RenameColonyButton".Translate()))
                    {
                        Find.WindowStack.Add(new Dialog_RenameColony());
                    }
                }

                listing.Gap(12f);
            }

            // Enable/Disable toggle
            listing.CheckboxLabeled("ChronoSave_EnabledLabel".Translate(), ref chronoSaveEnabled, "ChronoSave_EnabledTooltip".Translate());
            listing.Gap(12f);
            
            // Save interval setting
            Rect intervalRect = listing.GetRect(30f);
            Rect intervalLabelRect = new Rect(intervalRect.x, intervalRect.y, intervalRect.width * 0.7f, intervalRect.height);
            Rect intervalFieldRect = new Rect(intervalRect.x + intervalRect.width * 0.7f, intervalRect.y, intervalRect.width * 0.3f, intervalRect.height);
            
            Widgets.Label(intervalLabelRect, "ChronoSave_IntervalLabel".Translate());

            // Vanilla's numeric field rather than a raw TextField. It keeps the buffer and the value
            // apart, so an empty buffer is accepted and left empty while the player retypes
            // (IsPartiallyOrFullyTypedNumber returns true for "" and IsFullyTypedNumber returns
            // false for it, so nothing refills the field), and every fully typed edit is clamped and
            // written back into the buffer, so the field can no longer show a number that is not the
            // one in effect. The hand-rolled version did the opposite on both counts.
            Widgets.TextFieldNumeric(
                intervalFieldRect,
                ref saveIntervalMinutes,
                ref saveIntervalBuffer,
                ChronoSaveSchedule.MinIntervalMinutes,
                ChronoSaveSchedule.MaxIntervalMinutes);

            // The whole row, not just the label. The field is where the player's cursor actually is.
            TooltipHandler.TipRegion(intervalRect, "ChronoSave_IntervalTooltip".Translate());

            listing.Gap(12f);

            // Number of saves slider. listing.Slider returns the value, not the rect it drew into,
            // which is why the old code reached for listing.GetRect(0f) and bound the tooltip to a
            // zero-height rect that could never be hovered. Taking the rect first is
            // Listing_Standard.Slider inlined, so the layout does not move.
            TaggedString savesTooltip = "ChronoSave_NumberOfSavesTooltip".Translate();

            Rect savesLabelRect = listing.Label("ChronoSave_NumberOfSavesLabel".Translate(numberOfSaves));
            Rect savesSliderRect = listing.GetRect(SliderHeight);
            numberOfSaves = Mathf.RoundToInt(Widgets.HorizontalSlider(
                savesSliderRect, numberOfSaves, ChronoSaveSchedule.MinSlots, ChronoSaveSchedule.MaxSlots));
            listing.Gap(listing.verticalSpacing);

            TooltipHandler.TipRegion(savesLabelRect, savesTooltip);
            TooltipHandler.TipRegion(savesSliderRect, savesTooltip);

            listing.Gap(20f);
            
            // Info section
            GUI.color = Color.gray;
            listing.Label("ChronoSave_InfoHeader".Translate());
            listing.Label("ChronoSave_InfoText".Translate());
            listing.Label("ChronoSave_PermadeathInfo".Translate());
            GUI.color = Color.white;

            DrawStrandedBackups(listing, info);

            listing.End();
        }
        
        /// <summary>
        /// Reports any leftover backup copies RimWorld left beside chronosave slots.
        /// </summary>
        /// <param name="listing">The listing being drawn into.</param>
        /// <param name="info">The loaded game's info, or <c>null</c> when none is loaded.</param>
        /// <remarks>
        /// Reported and never deleted. A <c>Chronosave-N.rws.old</c> can also be vanilla's own
        /// one-generation safety copy for a colony that has nothing to do with this mod, and there
        /// is no way to tell which is which from disk for any colony other than the loaded one. So
        /// the player is given the count, the size and the folder, and decides.
        ///
        /// No "open folder" button: vanilla only offers <c>Application.OpenURL</c> on a folder path
        /// under Windows, which suggests it misbehaves elsewhere. Showing the path is what vanilla
        /// does underneath its own button and works everywhere.
        /// </remarks>
        private void DrawStrandedBackups(Listing_Standard listing, GameInfo info)
        {
            float now = Time.realtimeSinceStartup;
            if (ChronoSaveSchedule.ShouldRescanBackups(lastBackupScanRealTime, now))
            {
                lastBackupScanRealTime = now;

                // The loaded Commitment colony's own backup is vanilla's live safety copy for the
                // file it is still writing, not a leftover, so it is excluded.
                string exclude = info != null && info.permadeathMode ? info.permadeathModeUniqueName : null;
                strandedBackups = StrandedBackups.Scan(GenFilePaths.SavedGamesFolderPath, exclude);
            }

            if (strandedBackups.Count == 0)
            {
                return;
            }

            listing.Gap(12f);
            listing.Label("ChronoSave_StrandedBackupsLabel".Translate(
                strandedBackups.Count,
                strandedBackups.TotalMegabytes.ToString("F1")));

            GUI.color = Color.gray;
            listing.Label("ChronoSave_StrandedBackupsText".Translate());
            listing.Label(GenFilePaths.SavedGamesFolderPath);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Folds any part-finished text edit back into the stored values.
        /// </summary>
        /// <remarks>
        /// The interval field deliberately tolerates an empty buffer while the player retypes, which
        /// is the fix for not being able to clear it. The consequence is that a window closed on an
        /// empty field would reopen on an empty field, so the buffer is settled here instead.
        /// Called from <c>ChronoSaveMod.WriteSettings</c>, which
        /// <c>RimWorld.Dialog_ModSettings.PreClose</c> reaches on every close of the window.
        /// </remarks>
        public void CommitEditBuffers()
        {
            saveIntervalMinutes = ChronoSaveSchedule.ParseIntervalBuffer(saveIntervalBuffer, saveIntervalMinutes);
            saveIntervalBuffer = ChronoSaveSchedule.FormatIntervalBuffer(saveIntervalMinutes);
            numberOfSaves = ChronoSaveSchedule.ClampSlotCount(numberOfSaves);
        }

        /// <summary>
        /// Saves and loads the mod settings.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Values.Look(ref saveIntervalMinutes, "saveIntervalMinutes", 5f);
            Scribe_Values.Look(ref numberOfSaves, "numberOfSaves", 10);
            Scribe_Values.Look(ref chronoSaveEnabled, "chronoSaveEnabled", true);
            
            // Validate loaded values
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                saveIntervalMinutes = ChronoSaveSchedule.ClampIntervalMinutes(saveIntervalMinutes);
                numberOfSaves = ChronoSaveSchedule.ClampSlotCount(numberOfSaves);
                saveIntervalBuffer = ChronoSaveSchedule.FormatIntervalBuffer(saveIntervalMinutes);
            }
        }
    }
}
