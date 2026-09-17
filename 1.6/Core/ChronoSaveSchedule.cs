using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ChronoSave.Core
{
    /// <summary>
    /// The scheduling decisions behind chronosaving, expressed over plain values so they can be
    /// exercised without a running RimWorld.
    /// </summary>
    /// <remarks>
    /// <see cref="ChronoSaveGameComponent"/> reaches its settings through the static
    /// <c>ChronoSaveMod.Settings</c>, which is null unless the mod has actually been loaded by the
    /// game, and its timing reads <c>UnityEngine.Time.realtimeSinceStartup</c>, which is a native
    /// call. Neither is available in a test process, so every decision that can be stated over
    /// numbers, strings and file stamps lives here instead and the component passes the values in.
    /// </remarks>
    public static class ChronoSaveSchedule
    {
        /// <summary>
        /// The prefix every chronosave filename carries.
        /// </summary>
        public const string SaveNamePrefix = "Chronosave-";

        /// <summary>
        /// The largest slot number the settings interface allows.
        /// </summary>
        public const int MaxSlots = 25;

        /// <summary>
        /// The longest colony key a filename will carry.
        /// </summary>
        /// <remarks>
        /// Matches the limit <c>Verse.GenText.IsValidFilename</c> applies, which is what the
        /// vanilla naming prompt enforces. A longer name reaches this code only through another mod.
        /// </remarks>
        public const int MaxColonyKeyLength = 40;

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
        /// Characters never allowed in a colony key.
        /// </summary>
        /// <remarks>
        /// Deliberately a fixed set rather than <c>Path.GetInvalidFileNameChars()</c>. That property
        /// is platform dependent, and on Mono under macOS it returns only the null character and the
        /// forward slash, so a colony name containing a colon or a quote would produce a file that
        /// Windows cannot open. This is the set
        /// <c>Verse.GenText.GetInvalidFilenameCharacters</c> adds on top of the platform's own,
        /// plus the double quote, which vanilla's list omits.
        /// </remarks>
        private const string InvalidKeyCharacters = "/\\{}<>:*|!@#$%^&?\"";

        /// <summary>
        /// Builds the save filename for a slot in the shared pool.
        /// </summary>
        /// <param name="slot">The slot number.</param>
        /// <returns>The filename, without a directory or an extension.</returns>
        public static string SaveNameForSlot(int slot)
        {
            return SaveNameForSlot(null, slot);
        }

        /// <summary>
        /// Builds the save filename for a slot in a colony's ring.
        /// </summary>
        /// <param name="ringKey">The colony key, or <c>null</c> for the shared pool.</param>
        /// <param name="slot">The slot number.</param>
        /// <returns>The filename, without a directory or an extension.</returns>
        /// <remarks>
        /// The pool form is the name the mod has always written, byte for byte, which is what makes
        /// the files already on a subscriber's disk the pool rather than orphans.
        ///
        /// Names are only ever generated and compared here, never parsed back apart, so a key
        /// containing a dash or a digit cannot be confused with a slot number: the key "X-2" at slot
        /// 1 gives <c>Chronosave-X-2-1</c> and the key "X" at slot 2 gives <c>Chronosave-X-2</c>,
        /// and those are simply different strings.
        /// </remarks>
        public static string SaveNameForSlot(string ringKey, int slot)
        {
            if (string.IsNullOrEmpty(ringKey))
            {
                return SaveNamePrefix + slot;
            }

            return SaveNamePrefix + ringKey + "-" + slot;
        }

        /// <summary>
        /// Turns a colony's name into the key its chronosaves are filed under.
        /// </summary>
        /// <param name="colonyNameOrNull">
        /// The colony name, or <c>null</c> when the colony has not been named yet.
        /// </param>
        /// <returns>The key, or <c>null</c> to use the shared pool.</returns>
        /// <remarks>
        /// Returning <c>null</c> for an unnamed colony is the whole migration story. A colony has no
        /// name for its first 4.3 game days at minimum, because <c>FactionGenerator</c> skips name
        /// generation for the player faction and the one vanilla writer is the naming prompt. Every
        /// throwaway start therefore shares one pool and leaks no files, and only a colony the
        /// player has committed to gets a ring of its own.
        ///
        /// Never pass <c>Faction.Name</c> without checking <c>Faction.HasName</c> first: the getter
        /// falls back to the localised <c>def.LabelCap</c>, so an unnamed colony would file itself
        /// under "New Arrivals" in English and something different in every other language.
        /// </remarks>
        public static string RingKeyFromColonyName(string colonyNameOrNull)
        {
            if (string.IsNullOrEmpty(colonyNameOrNull))
            {
                return null;
            }

            var builder = new StringBuilder(colonyNameOrNull.Length);
            var pendingSeparator = false;

            foreach (var character in colonyNameOrNull)
            {
                if (character < ' ' || character == '\u007f' || InvalidKeyCharacters.IndexOf(character) >= 0)
                {
                    // Matches Verse.GenText.SanitizeFilename, which joins the surviving runs with an
                    // underscore rather than closing the gap, so "Ridge/Hold" stays two words.
                    pendingSeparator = builder.Length > 0;
                    continue;
                }

                if (pendingSeparator)
                {
                    builder.Append('_');
                    pendingSeparator = false;
                }

                builder.Append(character);
            }

            var key = builder.ToString().Trim();

            if (key.Length > MaxColonyKeyLength)
            {
                key = key.Substring(0, MaxColonyKeyLength);
            }

            // Trailing dots are stripped silently by Windows, which would make the name written
            // differ from the name looked for on the next pass. Trimmed after the truncation too,
            // because the cut can land on a space or a dot.
            key = key.TrimEnd('.').Trim();

            return key.Length == 0 ? null : key;
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
        /// Chooses the slot the next chronosave should be written to.
        /// </summary>
        /// <param name="ringKey">The colony key, or <c>null</c> for the shared pool.</param>
        /// <param name="numberOfSaves">The configured number of slots.</param>
        /// <param name="existingSaves">Every save file currently in the saves folder.</param>
        /// <returns>The slot to write.</returns>
        /// <remarks>
        /// This is <c>RimWorld.Autosaver.NewAutosaveFileName</c>: take the first name in the set
        /// that is not already used, otherwise the one written longest ago. Ties go to the lowest
        /// slot, matching <c>GenCollection.MinBy</c>, whose comparison is a strict less-than so the
        /// first candidate wins.
        ///
        /// Two deliberate departures from vanilla. Names are matched case-insensitively, because
        /// <c>SaveGameFilesUtility.SavedGameNamedExists</c> compares ordinally and therefore reports
        /// <c>chronosave-1.rws</c> as absent for <c>Chronosave-1</c> on a case-insensitive
        /// filesystem, which is most players, and then writes over it as if the slot were free. And
        /// a file whose timestamp could not be read is treated as the newest thing in the folder, so
        /// it is never the one chosen for overwrite while any dated alternative exists.
        /// </remarks>
        public static int ChooseSlot(string ringKey, int numberOfSaves, IEnumerable<SaveFileStamp> existingSaves)
        {
            if (numberOfSaves < 1)
            {
                // Vanilla throws here, MinBy refusing an empty sequence. The settings window clamps
                // this to 1 through 25, so it is unreachable short of a hand-edited config.
                return 1;
            }

            var slotForName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var slot = 1; slot <= numberOfSaves; slot++)
            {
                slotForName[SaveNameForSlot(ringKey, slot)] = slot;
            }

            var writtenAt = new DateTime[numberOfSaves + 1];
            var used = new bool[numberOfSaves + 1];

            if (existingSaves != null)
            {
                foreach (var stamp in existingSaves)
                {
                    if (stamp.Name == null || !slotForName.TryGetValue(stamp.Name, out var slot))
                    {
                        continue;
                    }

                    // Two files can map to one slot when they differ only by case on a
                    // case-insensitive filesystem. The newer one is the one that would be read back.
                    if (!used[slot] || stamp.LastWriteUtc > writtenAt[slot])
                    {
                        writtenAt[slot] = stamp.LastWriteUtc;
                    }

                    used[slot] = true;
                }
            }

            for (var slot = 1; slot <= numberOfSaves; slot++)
            {
                if (!used[slot])
                {
                    return slot;
                }
            }

            var oldest = 1;
            for (var slot = 2; slot <= numberOfSaves; slot++)
            {
                if (writtenAt[slot] < writtenAt[oldest])
                {
                    oldest = slot;
                }
            }

            return oldest;
        }

        /// <summary>
        /// Decides whether a save name still belongs to a colony's current ring.
        /// </summary>
        /// <param name="saveName">The name to check.</param>
        /// <param name="ringKey">The colony key, or <c>null</c> for the shared pool.</param>
        /// <param name="numberOfSaves">The configured number of slots.</param>
        /// <returns><c>true</c> when the name is one this ring would write.</returns>
        /// <remarks>
        /// Used to decide whether a failed chronosave may be retried into the same file. Retrying
        /// the same name matters: the file there is already spoiled, because <c>SafeSaver</c> moved
        /// the previous good copy to <c>.old</c> and deleted it outside Commitment mode, so writing
        /// it again can only improve that slot. Choosing afresh would instead pick a different slot
        /// every time, because the ruined file is now the newest in the folder, and a repeating
        /// fault would walk the whole ring and destroy every chronosave the player has.
        ///
        /// The name stops belonging when the player lowers the slot count or the colony gains a
        /// name between attempts, and the next attempt then picks a slot normally.
        /// </remarks>
        public static bool IsInRing(string saveName, string ringKey, int numberOfSaves)
        {
            if (string.IsNullOrEmpty(saveName))
            {
                return false;
            }

            for (var slot = 1; slot <= numberOfSaves; slot++)
            {
                if (string.Equals(saveName, SaveNameForSlot(ringKey, slot), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads the slot number out of a chronosave filename.
        /// </summary>
        /// <param name="saveName">The name to parse, without a directory or an extension.</param>
        /// <param name="slot">The slot, when the name is one this mod would write.</param>
        /// <returns><c>true</c> when the name belongs to a chronosave ring.</returns>
        /// <remarks>
        /// The counterpart to <see cref="SaveNameForSlot(string, int)"/> and must be changed with it;
        /// <c>IsRingSaveName_RoundTripsTheGenerator</c> fails if only one of the two is touched.
        /// Both shapes are accepted and the flat one must stay accepted forever, because it is what
        /// every install wrote before 2026-09-17 and those files, and the permadeath rebinds they
        /// caused, will keep turning up for years.
        ///
        /// Matching is deliberately exact rather than a prefix test. A colony a player legitimately
        /// named after this mod has a permadeath save called <c>Chronosave-3 (Permadeath)</c>, since
        /// both vanilla generators end with <c>AppendedPermadeathModeSuffix</c>, and a
        /// <c>StartsWith</c> would claim it.
        /// </remarks>
        public static bool TryParseRingSlot(string saveName, out int slot)
        {
            slot = 0;

            if (string.IsNullOrEmpty(saveName) || saveName.Length <= SaveNamePrefix.Length)
            {
                return false;
            }

            if (!saveName.StartsWith(SaveNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var remainder = saveName.Substring(SaveNamePrefix.Length);
            var lastDash = remainder.LastIndexOf('-');

            if (lastDash >= 0)
            {
                // A colony key, which must be there rather than empty.
                if (lastDash == 0)
                {
                    return false;
                }

                remainder = remainder.Substring(lastDash + 1);
            }

            if (!int.TryParse(remainder, NumberStyles.None, CultureInfo.InvariantCulture, out slot))
            {
                return false;
            }

            // Rejects a padded slot such as "03", which this mod never writes, so a file named that
            // way is the player's own.
            if (!string.Equals(remainder, slot.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                slot = 0;
                return false;
            }

            if (slot < 1 || slot > MaxSlots)
            {
                slot = 0;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Decides whether a save name is one this mod writes.
        /// </summary>
        /// <param name="saveName">The name to check.</param>
        /// <returns><c>true</c> when the name belongs to a chronosave ring.</returns>
        public static bool IsRingSaveName(string saveName)
        {
            return TryParseRingSlot(saveName, out _);
        }

        /// <summary>
        /// Decides whether a Commitment colony needs to be told its save has been bound into this
        /// mod's rotation.
        /// </summary>
        /// <param name="permadeathMode">Whether the colony is in Commitment mode.</param>
        /// <param name="permadeathUniqueName">The colony's permadeath save name.</param>
        /// <param name="alreadyWarnedFor">The name already warned about, or <c>null</c>.</param>
        /// <returns><c>true</c> when a warning is owed.</returns>
        /// <remarks>
        /// Loading a chronosave rebinds the colony:
        /// <c>SavedGameLoaderNow.LoadGameFromSaveFileNow</c> unconditionally calls
        /// <c>PermadeathModeUtility.CheckUpdatePermadeathModeUniqueNameOnGameLoad</c>, which sets
        /// <c>permadeathModeUniqueName</c> to the filename with nothing but a dev-log warning. Every
        /// autosave and both save-and-quit paths then write the colony into a slot this mod recycles.
        ///
        /// Keyed on the name rather than a flag, so a later rebind into a different slot warns again
        /// while reloading the same colony does not.
        /// </remarks>
        public static bool NeedsRebindWarning(bool permadeathMode, string permadeathUniqueName, string alreadyWarnedFor)
        {
            return permadeathMode
                   && IsRingSaveName(permadeathUniqueName)
                   && !string.Equals(permadeathUniqueName, alreadyWarnedFor, StringComparison.Ordinal);
        }

        /// <summary>
        /// How long the settings window keeps its count of leftover backup files before recounting.
        /// </summary>
        public const float BackupRescanIntervalSeconds = 5f;

        /// <summary>
        /// Decides whether the settings window should recount the leftover backup files.
        /// </summary>
        /// <param name="lastScanRealTime">Real time of the previous count.</param>
        /// <param name="nowRealTime">Real time now.</param>
        /// <returns><c>true</c> when it is time to count again.</returns>
        /// <remarks>
        /// The settings window redraws every frame, so the directory listing needs a rate limit. A
        /// few seconds is short enough that a player deleting files in Finder sees the number drop
        /// without reopening the window.
        /// </remarks>
        public static bool ShouldRescanBackups(float lastScanRealTime, float nowRealTime)
        {
            return nowRealTime - lastScanRealTime >= BackupRescanIntervalSeconds;
        }

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
        /// With no baseline, existence and a non-zero length are all that can be checked.
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
