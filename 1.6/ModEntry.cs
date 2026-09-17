using HarmonyLib;
using Verse;
using ChronoSave.Core;
using UnityEngine;

namespace ChronoSave
{
    /// <summary>
    /// Main mod entry point for the Chrono Save mod.
    /// Handles mod initialization, settings management, and Harmony patching.
    /// </summary>
    public class ChronoSaveMod : Mod
    {   
        /// <summary>
        /// Gets the mod settings instance for ChronoSave.
        /// Provides access to save interval and other configuration options.
        /// </summary>
        public static ChronoSaveSettings Settings { get; private set; }
        
        /// <summary>
        /// The Harmony instance used for applying patches to the base game.
        /// </summary>
        private readonly Harmony harmony;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChronoSaveMod"/> class.
        /// Sets up mod settings, applies Harmony patches, and logs successful initialization.
        /// </summary>
        /// <param name="pack">The mod content pack containing mod information and assets.</param>
        public ChronoSaveMod(ModContentPack pack) : base(pack)
        {
            Settings = GetSettings<ChronoSaveSettings>();
            
            harmony = new Harmony("com.zei33.chronosave");
            harmony.PatchAll();

            Log.Message("[Chrono Save] Loaded version 1.0 successfully.");
        }
        
        /// <summary>
        /// Gets the category name for this mod in the settings menu.
        /// </summary>
        /// <returns>The display name for the mod's settings category.</returns>
        public override string SettingsCategory() => "ChronoSave_SettingsCategory".Translate();
        
        /// <summary>
        /// Renders the mod settings window content.
        /// Delegates to the settings class for proper separation of concerns.
        /// </summary>
        /// <param name="inRect">The rectangle area available for drawing the settings interface.</param>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoSettingsWindowContents(inRect);
        }

        /// <summary>
        /// Writes the mod settings, settling any part-finished text edit first.
        /// </summary>
        /// <remarks>
        /// <c>RimWorld.Dialog_ModSettings.PreClose</c> calls this on every close of the settings
        /// window, which is the hook the interval field needs: it deliberately tolerates being
        /// cleared while the player retypes, so a window closed on an empty field would otherwise
        /// reopen on an empty field.
        /// </remarks>
        public override void WriteSettings()
        {
            Settings.CommitEditBuffers();
            base.WriteSettings();
        }
    }
}