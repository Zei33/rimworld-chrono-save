namespace ChronoSave.Core
{
    /// <summary>
    /// The scheduling decisions behind chronosaving, expressed over plain numbers so they can be
    /// exercised without a running RimWorld.
    /// </summary>
    /// <remarks>
    /// <see cref="ChronoSaveGameComponent"/> reaches its settings through the static
    /// <c>ChronoSaveMod.Settings</c>, which is null unless the mod has actually been loaded by the
    /// game, and its timing reads <c>UnityEngine.Time.realtimeSinceStartup</c>, which is a native
    /// call. Neither is available in a test process, so every decision that can be stated as
    /// arithmetic lives here instead and the component passes the values in.
    /// </remarks>
    public static class ChronoSaveSchedule
    {
        /// <summary>
        /// The prefix every chronosave filename carries, without the slot number.
        /// </summary>
        public const string SaveNamePrefix = "Chronosave-";

        /// <summary>
        /// The largest slot number the settings interface allows.
        /// </summary>
        public const int MaxSlots = 25;

        /// <summary>
        /// Builds the save filename for a slot.
        /// </summary>
        /// <param name="slot">The slot number.</param>
        /// <returns>The filename, without a directory or an extension.</returns>
        public static string SaveNameForSlot(int slot)
        {
            return SaveNamePrefix + slot;
        }

        /// <summary>
        /// Decides whether enough real time has passed for the next chronosave.
        /// </summary>
        /// <param name="lastSaveRealTime">Real time at which the previous chronosave was taken.</param>
        /// <param name="nowRealTime">Real time now.</param>
        /// <param name="intervalMinutes">The configured interval, in minutes.</param>
        /// <returns><c>true</c> when a chronosave is due.</returns>
        public static bool IsDue(float lastSaveRealTime, float nowRealTime, float intervalMinutes)
        {
            return nowRealTime - lastSaveRealTime >= intervalMinutes * 60f;
        }

        /// <summary>
        /// Brings a slot back inside the configured range, wrapping to the first slot.
        /// </summary>
        /// <param name="slot">The slot to check.</param>
        /// <param name="numberOfSaves">The configured number of slots.</param>
        /// <returns>The slot to use.</returns>
        /// <remarks>
        /// A <paramref name="numberOfSaves"/> of zero leaves the slot alone, which is what the loop
        /// this replaced did: its body never ran, so it returned the slot unchanged.
        /// </remarks>
        public static int SlotInRange(int slot, int numberOfSaves)
        {
            if (numberOfSaves > 0 && slot > numberOfSaves)
            {
                return 1;
            }

            return slot;
        }

        /// <summary>
        /// Moves to the next slot in the ring.
        /// </summary>
        /// <param name="slot">The current slot.</param>
        /// <param name="numberOfSaves">The configured number of slots.</param>
        /// <returns>The next slot, wrapping to the first.</returns>
        public static int AdvanceSlot(int slot, int numberOfSaves)
        {
            var next = slot + 1;
            if (next > numberOfSaves)
            {
                return 1;
            }

            return next;
        }

        /// <summary>
        /// Repairs a slot number read from a save file.
        /// </summary>
        /// <param name="slot">The slot as loaded.</param>
        /// <returns>The slot to use.</returns>
        /// <remarks>
        /// Bounded by <see cref="MaxSlots"/> rather than by the configured number of saves, which is
        /// the behaviour this replaced. A slot inside the hard limit but above the player's current
        /// setting survives here and is brought into range later by <see cref="SlotInRange"/>.
        /// </remarks>
        public static int SanitiseLoadedSlot(int slot)
        {
            if (slot < 1 || slot > MaxSlots)
            {
                return 1;
            }

            return slot;
        }
    }
}
