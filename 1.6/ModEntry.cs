using Verse;
using ChronoSave.Core;
using UnityEngine;

namespace ChronoSave
{
    /// <summary>
    /// Main mod entry point for the Chrono Save mod. Holds the settings and draws their window.
    /// </summary>
    public class ChronoSaveMod : Mod
    {   
        /// <summary>
        /// Gets the mod settings instance for ChronoSave.
        /// Provides access to save interval and other configuration options.
        /// </summary>
        public static ChronoSaveSettings Settings { get; private set; }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="ChronoSaveMod"/> class.
        /// </summary>
        /// <param name="pack">The mod content pack containing mod information and assets.</param>
        /// <remarks>
        /// No Harmony instance and no patches. This mod had exactly one, a postfix on the private
        /// <c>Verse.Game.FillComponents</c> that added <see cref="Core.ChronoSaveGameComponent"/>,
        /// and it never did anything: <c>FillComponents</c> already walks
        /// <c>typeof(GameComponent).AllSubclassesNonAbstract()</c> and constructs whatever is
        /// missing with <c>Activator.CreateInstance(type, this)</c>, and that enumeration covers
        /// every loaded mod assembly through <c>GenTypes.AllActiveAssemblies</c>. So the component
        /// already existed by the time the postfix ran and its own guard returned early every time.
        /// Its log line had never appeared in anybody's log.
        ///
        /// Removing it retires the declared <c>brrainz.harmony</c> dependency too, which is why the
        /// two go together: dropping the patch alone would leave the install prompt, and dropping
        /// the dependency while this constructor still called <c>PatchAll()</c> would leave a hard
        /// runtime dependency on a library that might then be absent.
        /// </remarks>
        public ChronoSaveMod(ModContentPack pack) : base(pack)
        {
            Settings = GetSettings<ChronoSaveSettings>();

            Log.Message("[Chrono Save] Loaded.");
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