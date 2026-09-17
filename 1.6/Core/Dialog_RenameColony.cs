using RimWorld;
using Verse;

namespace ChronoSave.Core
{
    /// <summary>
    /// RimWorld's own faction naming dialog, opened from Chrono Save's settings window so a
    /// Commitment colony bound into a chronosave slot can be moved out of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because vanilla 1.6 has no player-facing rename. <c>Faction.OfPlayer.Name</c> has
    /// exactly one writer, <c>NamePlayerFactionDialogUtility.Named</c>, reached from
    /// <c>Dialog_NamePlayerFaction</c>, which is opened from the one-time prompt in
    /// <c>Faction.FactionTick</c> (gated on <c>!Faction.OfPlayer.HasName</c>, so it never fires
    /// again) and from a dev-mode debug action. There is no rename button on any tab. So telling an
    /// affected player to rename their colony is not an instruction they can follow, and the mod has
    /// to open the dialog itself.
    /// </para>
    /// <para>
    /// <c>Named</c> is vanilla's own migration and this class adds nothing to it: it sets the faction
    /// name, works out a new permadeath save name from the player's input, and then in one
    /// synchronous long event assigns <c>permadeathModeUniqueName</c>, calls
    /// <c>Find.Autosaver.DoAutosave()</c> (which in Commitment mode writes to that new unique name)
    /// and deletes the file the colony was bound to. **Chrono Save writes no file and deletes no
    /// file of its own on this path**, which is the point of routing through vanilla.
    /// </para>
    /// <para>
    /// A <c>Window</c> is not scribed, unlike a <c>Letter</c>, so a mod type here does not affect
    /// whether the mod can be removed from a save mid-game.
    /// </para>
    /// </remarks>
    public class Dialog_RenameColony : Dialog_GiveName
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="Dialog_RenameColony"/> class.
        /// </summary>
        public Dialog_RenameColony()
        {
            nameGenerator = () => NameGenerator.GenerateName(Faction.OfPlayer.def.factionNameMaker, IsValidName);
            curName = Faction.OfPlayer.HasName ? Faction.OfPlayer.Name : nameGenerator();

            // The vanilla keys for the accepted and rejected cases, so those two paths read exactly
            // as they do when the game asks for a name itself, in all of RimWorld's languages.
            nameMessageKey = "ChronoSave_RenameColonyMessage";
            gainedNameMessageKey = "PlayerFactionGainsName";
            invalidNameMessageKey = "PlayerFactionNameIsInvalid";
        }

        /// <summary>
        /// Whether a typed name is one RimWorld will accept.
        /// </summary>
        /// <param name="s">The typed name.</param>
        /// <returns><c>true</c> when the name is valid.</returns>
        protected override bool IsValidName(string s)
        {
            return NamePlayerFactionDialogUtility.IsValidName(s);
        }

        /// <summary>
        /// Applies the name through RimWorld's own path.
        /// </summary>
        /// <param name="s">The accepted name.</param>
        protected override void Named(string s)
        {
            NamePlayerFactionDialogUtility.Named(s);
        }
    }
}
