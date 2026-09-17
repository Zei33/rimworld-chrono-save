using ChronoSave.Core;
using NUnit.Framework;

namespace ChronoSave.Tests
{
    /// <summary>
    /// Covers what happens after a chronosave attempt finishes: when the next one is due, whether
    /// the ring moves on, and what the player is told.
    /// </summary>
    /// <remarks>
    /// These exist because the mod used to report the save, advance the ring and reset the clock at
    /// the moment it queued the work, before anything had been written. The queued event runs a
    /// frame or two later and can abort, so the player was told a recovery point existed in exactly
    /// the case where it did not.
    /// </remarks>
    [TestFixture]
    public class ChronoSaveOutcomeTests
    {
        private const float Now = 1000f;
        private const float FiveMinutes = 5f;

        [Test]
        public void Resolve_SuccessStartsAFullIntervalFromNow()
        {
            var resolution = ChronoSaveSchedule.Resolve(ChronoSaveOutcome.Succeeded, Now, FiveMinutes);

            Assert.That(resolution.LastSaveRealTime, Is.EqualTo(Now));
            Assert.That(ChronoSaveSchedule.IsDue(resolution.LastSaveRealTime, Now, FiveMinutes), Is.False);
        }

        [Test]
        public void Resolve_SuccessIsTheOnlyOutcomeThatClaimsSuccess()
        {
            var resolution = ChronoSaveSchedule.Resolve(ChronoSaveOutcome.Succeeded, Now, FiveMinutes);

            Assert.That(resolution.ShowSuccessMessage, Is.True);
            Assert.That(resolution.ShowFailureMessage, Is.False);
        }

        [Test]
        public void Resolve_FailureWarnsRatherThanClaimingSuccess()
        {
            var resolution = ChronoSaveSchedule.Resolve(ChronoSaveOutcome.Failed, Now, FiveMinutes);

            Assert.That(resolution.ShowSuccessMessage, Is.False);
            Assert.That(resolution.ShowFailureMessage, Is.True);
        }

        [Test]
        public void Resolve_AnAbortedAttemptSaysNothingToThePlayer()
        {
            // Nothing was written and nothing was promised, so there is nothing to retract.
            var resolution = ChronoSaveSchedule.Resolve(ChronoSaveOutcome.Aborted, Now, FiveMinutes);

            Assert.That(resolution.ShowSuccessMessage, Is.False);
            Assert.That(resolution.ShowFailureMessage, Is.False);
        }

        [Test]
        public void Resolve_NoOutcomeLeavesTheTimerWhereItWas()
        {
            // The regression test for the retry storm. The in-flight latch clears as the queued
            // event returns, so the frame update runs again immediately afterwards. An outcome that
            // left lastSaveRealTime alone would still be due, and the whole cycle would repeat every
            // few frames: a status box flashing continuously, or a 36 MB write every third frame.
            foreach (ChronoSaveOutcome outcome in System.Enum.GetValues(typeof(ChronoSaveOutcome)))
            {
                var resolution = ChronoSaveSchedule.Resolve(outcome, Now, FiveMinutes);

                Assert.That(
                    ChronoSaveSchedule.IsDue(resolution.LastSaveRealTime, Now, FiveMinutes),
                    Is.False,
                    outcome + " is due again immediately, which retries every frame.");
            }
        }

        [Test]
        public void Resolve_AnAbortRetriesSoonerThanAFailure()
        {
            // Pins the relationship rather than the two constants, so they can be tuned without
            // churning the test. An abort wrote nothing and is usually a passing condition; a
            // failure has already spoiled a slot and is worth backing further off.
            //
            // Asserted through IsDue rather than by comparing the two baselines directly. The
            // baseline is a backdated timestamp, so a later retry is a larger number, and a direct
            // comparison reads the wrong way round to anyone who has not just read RetryBaseline.
            var aborted = ChronoSaveSchedule.Resolve(ChronoSaveOutcome.Aborted, Now, FiveMinutes);
            var failed = ChronoSaveSchedule.Resolve(ChronoSaveOutcome.Failed, Now, FiveMinutes);
            var between = Now + (ChronoSaveSchedule.AbortRetrySeconds + ChronoSaveSchedule.FailureRetrySeconds) / 2f;

            Assert.That(ChronoSaveSchedule.IsDue(aborted.LastSaveRealTime, between, FiveMinutes), Is.True);
            Assert.That(ChronoSaveSchedule.IsDue(failed.LastSaveRealTime, between, FiveMinutes), Is.False);
        }

        [Test]
        public void Resolve_CarriesTheOutcomeItWasGiven()
        {
            foreach (ChronoSaveOutcome outcome in System.Enum.GetValues(typeof(ChronoSaveOutcome)))
            {
                Assert.That(ChronoSaveSchedule.Resolve(outcome, Now, FiveMinutes).Outcome, Is.EqualTo(outcome));
            }
        }

        [Test]
        public void RetryBaseline_IsNotDueImmediately()
        {
            var baseline = ChronoSaveSchedule.RetryBaseline(Now, FiveMinutes, ChronoSaveSchedule.FailureRetrySeconds);

            Assert.That(ChronoSaveSchedule.IsDue(baseline, Now, FiveMinutes), Is.False);
        }

        [Test]
        public void RetryBaseline_IsNotDueBeforeTheDelayElapses()
        {
            var baseline = ChronoSaveSchedule.RetryBaseline(Now, FiveMinutes, 60f);

            Assert.That(ChronoSaveSchedule.IsDue(baseline, Now + 59.5f, FiveMinutes), Is.False);
        }

        [Test]
        public void RetryBaseline_IsDueAfterTheDelayElapses()
        {
            // Half a second of slack on either side rather than an assertion on the exact boundary.
            // A float at a realtime around 10^3 still has enough precision, but the baseline is
            // produced by subtraction and compared after addition, so an exact-boundary assertion
            // is a flake waiting for a longer session.
            var baseline = ChronoSaveSchedule.RetryBaseline(Now, FiveMinutes, 60f);

            Assert.That(ChronoSaveSchedule.IsDue(baseline, Now + 60.5f, FiveMinutes), Is.True);
        }

        [Test]
        public void RetryBaseline_NeverSchedulesLaterThanAFullInterval()
        {
            // A ten minute delay against a five minute interval is clamped back to the interval.
            var baseline = ChronoSaveSchedule.RetryBaseline(Now, FiveMinutes, 600f);

            Assert.That(ChronoSaveSchedule.IsDue(baseline, Now + 299f, FiveMinutes), Is.False);
            Assert.That(ChronoSaveSchedule.IsDue(baseline, Now + 300f, FiveMinutes), Is.True);
        }

        [Test]
        public void RetryBaseline_TreatsANegativeDelayAsDueNow()
        {
            // Without the clamp a negative delay would push the next save further away rather than
            // bringing it forward, which is the opposite of what a caller asking for a retry wants.
            var baseline = ChronoSaveSchedule.RetryBaseline(Now, FiveMinutes, -30f);

            Assert.That(ChronoSaveSchedule.IsDue(baseline, Now, FiveMinutes), Is.True);
        }

        [Test]
        public void IsPlausibleSaveSize_RejectsAMissingFile()
        {
            Assert.That(ChronoSaveSchedule.IsPlausibleSaveSize(ChronoSaveFiles.NoFile, 36_000_000L), Is.False);
        }

        [Test]
        public void IsPlausibleSaveSize_RejectsAnEmptyFile()
        {
            Assert.That(ChronoSaveSchedule.IsPlausibleSaveSize(0L, 36_000_000L), Is.False);
            Assert.That(ChronoSaveSchedule.IsPlausibleSaveSize(0L, 0L), Is.False);
        }

        [Test]
        public void IsPlausibleSaveSize_AcceptsAnySaveWhenThereIsNoBaseline()
        {
            // The gap, pinned deliberately rather than left to be discovered. The first chronosave
            // of a session has nothing to compare against, so it is checked for existence and a
            // non-zero length only. Seeding the baseline from a neighbouring Chronosave file is
            // unsound while the ring is shared between colonies, because that neighbour may be a
            // completely different colony and its size means nothing here.
            Assert.That(ChronoSaveSchedule.IsPlausibleSaveSize(3_735_505L, 0L), Is.True);
        }

        [Test]
        public void IsPlausibleSaveSize_RejectsThePreservedTruncatedSave()
        {
            // The one test anchored to real evidence rather than to a chosen threshold. See
            // docs/evidence/ in the workspace: 3,735,505 bytes against 36 to 38 MB neighbours,
            // holding a world, an empty <maps /> and no camera, with the document properly closed.
            // The player was told it worked.
            Assert.That(ChronoSaveSchedule.IsPlausibleSaveSize(3_735_505L, 36_000_000L), Is.False);
        }

        [Test]
        public void IsPlausibleSaveSize_AcceptsAGrowingSave()
        {
            Assert.That(ChronoSaveSchedule.IsPlausibleSaveSize(38_000_000L, 36_000_000L), Is.True);
        }

        [Test]
        public void IsPlausibleSaveSize_AcceptsAModestShrink()
        {
            // A caravan left, or a quest map despawned. This must not warn.
            Assert.That(ChronoSaveSchedule.IsPlausibleSaveSize(30_000_000L, 36_000_000L), Is.True);
        }

        [Test]
        public void IsPlausibleSaveSize_BoundaryIsInclusiveAtAQuarter()
        {
            Assert.That(ChronoSaveSchedule.IsPlausibleSaveSize(9_000_000L, 36_000_000L), Is.True);
            Assert.That(ChronoSaveSchedule.IsPlausibleSaveSize(8_999_999L, 36_000_000L), Is.False);
        }
    }
}
