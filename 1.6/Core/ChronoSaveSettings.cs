using System;
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
        /// Renders the mod settings window content.
        /// </summary>
        /// <param name="inRect">The rectangle area available for drawing the settings interface.</param>
        public void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            
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
            GUI.color = Color.white;
            
            listing.End();
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
