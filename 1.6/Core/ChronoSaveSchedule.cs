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
        /// Chooses the slot for the next chronosave after an attempt that finished.
        /// </summary>
        /// <param name="outcome">What the finished attempt did.</param>
        /// <param name="slot">The slot that attempt used.</param>
        /// <param name="numberOfSaves">The configured number of slots.</param>
        /// <returns>The slot to hold going forward.</returns>
        /// <remarks>
        /// Only a verified save moves the ring on. A failed one holds its slot deliberately: the
        /// file at that path is already spoiled, because <c>SafeSaver</c> moved the previous good
        /// version to <c>.old</c> and deleted it (<c>leaveOldFile</c> is
        /// <c>Find.GameInfo.permadeathMode</c>, false outside Commitment mode). Holding confines a
        /// repeating fault to the one slot it has already ruined. Advancing would let it walk the
        /// ring and destroy every chronosave the player has, one per interval.
        ///
        /// An aborted attempt wrote nothing, so there is nothing to move on from.
        /// </remarks>
        public static int NextSlot(ChronoSaveOutcome outcome, int slot, int numberOfSaves)
        {
            if (outcome != ChronoSaveOutcome.Succeeded)
            {
                return slot;
            }

            return AdvanceSlot(slot, numberOfSaves);
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

        /// <summary>
        /// How long to wait before retrying after an attempt that was called off before writing.
        /// </summary>
        public const float AbortRetrySeconds = 5f;

        /// <summary>
        /// How long to wait before retrying after an attempt whose file did not verify.
        /// </summary>
        public const float FailureRetrySeconds = 60f;

        /// <summary>
        /// The numerator of the smallest fraction of the previous good save a new one may be.
        /// </summary>
        public const long PlausibleSaveShrinkNumerator = 1L;

        /// <summary>
        /// The denominator of the smallest fraction of the previous good save a new one may be.
        /// </summary>
        public const long PlausibleSaveShrinkDenominator = 4L;

        /// <summary>
        /// Produces a last-save real time that puts the next chronosave a short delay away rather
        /// than on the very next frame.
        /// </summary>
        /// <param name="nowRealTime">Real time now.</param>
        /// <param name="intervalMinutes">The configured interval, in minutes.</param>
        /// <param name="retryDelaySeconds">How long the next attempt should be held off.</param>
        /// <returns>A value to store as the last chronosave real time.</returns>
        /// <remarks>
        /// Expressed as a backdated timestamp rather than as a second timing rule, so
        /// <see cref="IsDue"/> stays the only thing that decides when a save happens.
        ///
        /// The delay is clamped into zero through one full interval. A negative delay would push the
        /// next attempt into the future rather than bringing it forward, and a delay longer than the
        /// interval would make a retry slower than an ordinary save.
        /// </remarks>
        public static float RetryBaseline(float nowRealTime, float intervalMinutes, float retryDelaySeconds)
        {
            var intervalSeconds = intervalMinutes * 60f;

            if (retryDelaySeconds < 0f)
            {
                retryDelaySeconds = 0f;
            }
            else if (retryDelaySeconds > intervalSeconds)
            {
                retryDelaySeconds = intervalSeconds;
            }

            return nowRealTime - (intervalSeconds - retryDelaySeconds);
        }

        /// <summary>
        /// Decides whether a file that has just been written looks like a complete chronosave.
        /// </summary>
        /// <param name="savedBytes">
        /// Length of the written file, or <see cref="ChronoSaveFiles.NoFile"/> when it is missing.
        /// </param>
        /// <param name="previousGoodBytes">
        /// Length of the last chronosave this session that verified, or zero when none has.
        /// </param>
        /// <returns><c>true</c> when the file is plausible.</returns>
        /// <remarks>
        /// The size is the only signal available, because the save path swallows every exception and
        /// commits a truncated document rather than discarding it. The preserved example in
        /// <c>docs/evidence/</c> is 3.7 MB against 36 to 38 MB neighbours, so the comparison has to
        /// be against what this colony itself last wrote: an absolute floor that catches 3.7 MB
        /// would also reject a legitimate first-day save.
        ///
        /// A quarter is a judgement call, not a measurement. It sits well clear of both known
        /// numbers and leaves room for the real shrinks that happen, a caravan leaving or a quest
        /// map despawning. A false positive costs one warning and one repeated write; a false
        /// negative costs the player a recovery point they believe they have.
        ///
        /// With no baseline, existence and a non-zero length are all that can be checked. That gap
        /// closes when the ring stops being shared between colonies and a neighbouring chronosave
        /// can be assumed to be the same colony.
        /// </remarks>
        public static bool IsPlausibleSaveSize(long savedBytes, long previousGoodBytes)
        {
            if (savedBytes <= 0L)
            {
                return false;
            }

            if (previousGoodBytes <= 0L)
            {
                return true;
            }

            return savedBytes * PlausibleSaveShrinkDenominator >= previousGoodBytes * PlausibleSaveShrinkNumerator;
        }

        /// <summary>
        /// Works out the timer and messaging consequences of a finished chronosave attempt.
        /// </summary>
        /// <param name="outcome">What the attempt did.</param>
        /// <param name="nowRealTime">Real time now.</param>
        /// <param name="intervalMinutes">The configured interval, in minutes.</param>
        /// <returns>What the component should do about it.</returns>
        public static ChronoSaveResolution Resolve(ChronoSaveOutcome outcome, float nowRealTime, float intervalMinutes)
        {
            switch (outcome)
            {
                case ChronoSaveOutcome.Succeeded:
                    return new ChronoSaveResolution(outcome, nowRealTime, showSuccessMessage: true, showFailureMessage: false);

                case ChronoSaveOutcome.Failed:
                    return new ChronoSaveResolution(
                        outcome,
                        RetryBaseline(nowRealTime, intervalMinutes, FailureRetrySeconds),
                        showSuccessMessage: false,
                        showFailureMessage: true);

                default:
                    // Nothing was written and the player was never told anything, so there is
                    // nothing to retract. The log line is the whole notification.
                    return new ChronoSaveResolution(
                        outcome,
                        RetryBaseline(nowRealTime, intervalMinutes, AbortRetrySeconds),
                        showSuccessMessage: false,
                        showFailureMessage: false);
            }
        }
    }
}
