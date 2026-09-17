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
        private float saveIntervalMinutes = 5f;
        
        /// <summary>
        /// Number of chronosave files to maintain before looping (default: 10).
        /// </summary>
        private int numberOfSaves = 10;
        
        /// <summary>
        /// Whether the chronosave system is enabled (default: true).
        /// </summary>
        private bool chronoSaveEnabled = true;
        
        /// <summary>
        /// UI buffer for save interval input.
        /// </summary>
        private string saveIntervalBuffer = "5";
        
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
            TooltipHandler.TipRegion(intervalLabelRect, "ChronoSave_IntervalTooltip".Translate());
            
            saveIntervalBuffer = Widgets.TextField(intervalFieldRect, saveIntervalBuffer);
            
            // Validate and apply interval
            if (float.TryParse(saveIntervalBuffer, out float parsedInterval))
            {
                saveIntervalMinutes = Mathf.Clamp(parsedInterval, 1f, 60f);
            }
            else
            {
                saveIntervalBuffer = saveIntervalMinutes.ToString();
            }
            
            listing.Gap(12f);
            
            // Number of saves slider
            listing.Label("ChronoSave_NumberOfSavesLabel".Translate(numberOfSaves));
            numberOfSaves = Mathf.RoundToInt(listing.Slider(numberOfSaves, 1f, 25f));
            TooltipHandler.TipRegion(listing.GetRect(0f), "ChronoSave_NumberOfSavesTooltip".Translate());
            
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
                saveIntervalMinutes = Mathf.Clamp(saveIntervalMinutes, 1f, 60f);
                numberOfSaves = Mathf.Clamp(numberOfSaves, 1, 25);
                saveIntervalBuffer = saveIntervalMinutes.ToString();
            }
        }
    }
}
