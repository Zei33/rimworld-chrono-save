namespace ChronoSave.Core
{
    /// <summary>
    /// The reason a chronosave is being held back, or <see cref="None"/> when one may proceed.
    /// </summary>
    public enum ChronoSaveBlocker
    {
        /// <summary>Nothing is in the way.</summary>
        None,

        /// <summary>The game is not being played, so there is nothing to save.</summary>
        NotPlaying,

        /// <summary>The world is not built yet.</summary>
        NoWorld,

        /// <summary>The player has switched chronosaving off.</summary>
        Disabled,

        /// <summary>The colony is in Commitment mode, where the mod writes nothing.</summary>
        CommitmentMode,

        /// <summary>A save or load is already in progress.</summary>
        ScribeBusy,

        /// <summary>A long event is queued or running.</summary>
        LongEventInFlight,

        /// <summary>RimWorld's own saving is temporarily switched off.</summary>
        VanillaSavingDisabled,

        /// <summary>The player is picking a target on the map.</summary>
        MapTargeting,

        /// <summary>The player is picking a target on the world map, such as a shuttle destination.</summary>
        WorldTargeting,

        /// <summary>The player is planning a caravan route.</summary>
        RoutePlanning,

        /// <summary>A window is open that owns all input.</summary>
        ModalWindowOpen,

        /// <summary>A float menu is open, so the player's next click is already spoken for.</summary>
        FloatMenuOpen
    }

    /// <summary>
    /// One frame's readings of the game state a chronosave depends on, as plain booleans so the
    /// decision over them can be exercised without a running RimWorld.
    /// </summary>
    /// <remarks>
    /// Every field here is read through <c>Find</c> or <c>Current</c> and is therefore untestable.
    /// Splitting the readings from the decision is what makes the decision testable at all, and the
    /// decision is the part that has been wrong.
    /// </remarks>
    public struct ChronoSaveConditions
    {
        /// <summary>The game is being played rather than sitting on a pre-game screen.</summary>
        public bool Playing;

        /// <summary>The world and its interface exist.</summary>
        public bool WorldReady;

        /// <summary>The player has chronosaving switched on.</summary>
        public bool Enabled;

        /// <summary>This colony is in Commitment mode.</summary>
        public bool CommitmentMode;

        /// <summary>A scribe operation is already running.</summary>
        public bool ScribeActive;

        /// <summary>A long event is queued or running.</summary>
        public bool LongEventPending;

        /// <summary>RimWorld's own saving is temporarily switched off.</summary>
        public bool VanillaSavingDisabled;

        /// <summary>The map targeter is waiting for a click.</summary>
        public bool MapTargeterActive;

        /// <summary>The world targeter is waiting for a click.</summary>
        public bool WorldTargeterActive;

        /// <summary>The caravan route planner is open.</summary>
        public bool RoutePlannerActive;

        /// <summary>A window that owns all input is open.</summary>
        public bool ModalWindowOpen;

        /// <summary>A float menu is open.</summary>
        public bool FloatMenuOpen;

        /// <summary>
        /// Every reading in the state that permits a chronosave.
        /// </summary>
        /// <returns>Conditions with nothing blocking.</returns>
        public static ChronoSaveConditions AllClear()
        {
            return new ChronoSaveConditions
            {
                Playing = true,
                WorldReady = true,
                Enabled = true
            };
        }

        /// <summary>
        /// A copy that ignores the long event the mod queued itself.
        /// </summary>
        /// <returns>The same readings, with the long event one cleared.</returns>
        /// <remarks>
        /// Used only by the re-check inside that event's own body, where
        /// <c>LongEventHandler.AnyEventNowOrWaiting</c> is necessarily true:
        /// <c>UpdateCurrentSynchronousEvent</c> invokes the action first and clears
        /// <c>currentEvent</c> afterwards, so while the closure runs it is still looking at itself.
        ///
        /// It clears exactly that one reading and nothing else, which is the point. Every other
        /// guard has to be taken again inside the closure, because a frame or two has passed since
        /// the event was queued and a dialog or a targeter can have opened in between.
        /// </remarks>
        public ChronoSaveConditions IgnoringOwnLongEvent()
        {
            ChronoSaveConditions copy = this;
            copy.LongEventPending = false;
            return copy;
        }
    }
}
