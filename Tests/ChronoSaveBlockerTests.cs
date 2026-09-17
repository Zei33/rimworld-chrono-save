using System;
using System.Collections.Generic;
using System.Linq;
using ChronoSave.Core;
using NUnit.Framework;

namespace ChronoSave.Tests
{
    /// <summary>
    /// Covers the decision about whether a chronosave may happen right now.
    /// </summary>
    /// <remarks>
    /// This exists because of a report that went unanswered for thirteen months: saving while
    /// picking a shuttle's landing site breaks things badly. The readings themselves are all
    /// <c>Find.*</c> and <c>Current.*</c> and cannot be reached from here, so they are taken in the
    /// component and the decision over them lives in <see cref="ChronoSaveSchedule.FirstBlocker"/>,
    /// which is what these exercise.
    /// </remarks>
    [TestFixture]
    public class ChronoSaveBlockerTests
    {
        private static ChronoSaveConditions With(Action<Setter> set)
        {
            var setter = new Setter { Conditions = ChronoSaveConditions.AllClear() };
            set(setter);
            return setter.Conditions;
        }

        /// <summary>A box, because ChronoSaveConditions is a struct and lambdas cannot take it by ref.</summary>
        private class Setter
        {
            public ChronoSaveConditions Conditions;
        }

        [Test]
        public void AllClear_IsActuallyClear()
        {
            // Without this every other test here is vacuous: they would all pass against a factory
            // that returns something already blocked for an unrelated reason.
            Assert.That(ChronoSaveSchedule.FirstBlocker(ChronoSaveConditions.AllClear()), Is.EqualTo(ChronoSaveBlocker.None));
            Assert.That(ChronoSaveSchedule.CanSaveNow(ChronoSaveConditions.AllClear()), Is.True);
        }

        [Test]
        public void FirstBlocker_ReportsNotPlaying()
        {
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.Playing = false)),
                Is.EqualTo(ChronoSaveBlocker.NotPlaying));
        }

        [Test]
        public void FirstBlocker_ReportsNoWorld()
        {
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.WorldReady = false)),
                Is.EqualTo(ChronoSaveBlocker.NoWorld));
        }

        [Test]
        public void FirstBlocker_ReportsDisabled()
        {
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.Enabled = false)),
                Is.EqualTo(ChronoSaveBlocker.Disabled));
        }

        [Test]
        public void FirstBlocker_ReportsCommitmentMode()
        {
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.CommitmentMode = true)),
                Is.EqualTo(ChronoSaveBlocker.CommitmentMode));
        }

        [Test]
        public void FirstBlocker_ReportsScribeBusy()
        {
            // ScribeSaver.InitSaving does not refuse when a scribe operation is already running. It
            // logs an error and calls Scribe.ForceStop, tearing down whatever else was mid-document,
            // and SaveGame then swallows the fallout. That is the shape of the truncated file in
            // docs/evidence, so this guard is not decoration.
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.ScribeActive = true)),
                Is.EqualTo(ChronoSaveBlocker.ScribeBusy));
        }

        [Test]
        public void FirstBlocker_ReportsLongEventInFlight()
        {
            // Also the thing that stops a second chronosave being queued while the first waits.
            // A queued long event does not pause the game, so the frame update keeps running and the
            // interval condition is still true until the closure writes the timer.
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.LongEventPending = true)),
                Is.EqualTo(ChronoSaveBlocker.LongEventInFlight));
        }

        [Test]
        public void FirstBlocker_ReportsVanillaSavingDisabled()
        {
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.VanillaSavingDisabled = true)),
                Is.EqualTo(ChronoSaveBlocker.VanillaSavingDisabled));
        }

        [Test]
        public void FirstBlocker_ReportsMapTargeting()
        {
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.MapTargeterActive = true)),
                Is.EqualTo(ChronoSaveBlocker.MapTargeting));
        }

        [Test]
        public void FirstBlocker_ReportsWorldTargeting()
        {
            // The reported defect. Picking a shuttle or transport pod destination goes through
            // CompLaunchable.StartChoosingDestination, whose last line is
            // Find.WorldTargeter.BeginTargeting. Nothing in the game or the mod covered it:
            // GameDataSaveLoader.SavingIsTemporarilyDisabled reads Find.TilePicker,
            // WindowStack.WindowsPreventSave and the gravship cutscene flag, and none of those is
            // the world targeter.
            //
            // Note this is a different screen from the one the ProgramState gate already covers.
            // Page_SelectStartingSite, the new-game landing site, is a Page in ProgramState.Entry.
            // Confusing the two would close this issue without fixing anything.
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.WorldTargeterActive = true)),
                Is.EqualTo(ChronoSaveBlocker.WorldTargeting));
        }

        [Test]
        public void FirstBlocker_ReportsRoutePlanning()
        {
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.RoutePlannerActive = true)),
                Is.EqualTo(ChronoSaveBlocker.RoutePlanning));
        }

        [Test]
        public void FirstBlocker_ReportsModalWindowOpen()
        {
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.ModalWindowOpen = true)),
                Is.EqualTo(ChronoSaveBlocker.ModalWindowOpen));
        }

        [Test]
        public void FirstBlocker_ReportsFloatMenuOpen()
        {
            // A float menu sits on the Super layer and does not absorb input, so the modal test does
            // not see it, yet it is exactly a mid-interaction state whose next click a save steals.
            Assert.That(ChronoSaveSchedule.FirstBlocker(With(s => s.Conditions.FloatMenuOpen = true)),
                Is.EqualTo(ChronoSaveBlocker.FloatMenuOpen));
        }

        [Test]
        public void FirstBlocker_PrefersTheEarliestReasonWhenSeveralHold()
        {
            // The readings below WorldReady are not legal to take until the ones above hold, so the
            // precedence is not cosmetic even though only the log line reads it.
            var conditions = With(s =>
            {
                s.Conditions.Playing = false;
                s.Conditions.WorldTargeterActive = true;
            });

            Assert.That(ChronoSaveSchedule.FirstBlocker(conditions), Is.EqualTo(ChronoSaveBlocker.NotPlaying));
        }

        [Test]
        public void CanSaveNow_IsFalseForEveryBlockerAndTrueForNone()
        {
            // The sweep that catches a reading added to the struct and wired into the enum but
            // forgotten in FirstBlocker, which would silently stop being a guard.
            foreach (var single in SingleFlagCases())
            {
                var name = single.Key;
                var conditions = single.Value;

                Assert.That(ChronoSaveSchedule.CanSaveNow(conditions), Is.False, name + " should block a chronosave.");
                Assert.That(ChronoSaveSchedule.FirstBlocker(conditions), Is.Not.EqualTo(ChronoSaveBlocker.None), name);
            }

            Assert.That(ChronoSaveSchedule.CanSaveNow(ChronoSaveConditions.AllClear()), Is.True);
        }

        [Test]
        public void EveryBlockerInTheEnumIsReachable()
        {
            // The other half of the sweep above: a blocker added to the enum but never returned is
            // dead, and usually means a guard was named but not wired up.
            var reachable = SingleFlagCases().Values
                .Select(ChronoSaveSchedule.FirstBlocker)
                .Concat(new[] { ChronoSaveBlocker.None })
                .Distinct()
                .ToList();

            foreach (ChronoSaveBlocker blocker in Enum.GetValues(typeof(ChronoSaveBlocker)))
            {
                Assert.That(reachable, Contains.Item(blocker), blocker + " is in the enum but nothing produces it.");
            }
        }

        [Test]
        public void IgnoringOwnLongEvent_ClearsOnlyTheLongEventReading()
        {
            var original = With(s => s.Conditions.LongEventPending = true);
            var copy = original.IgnoringOwnLongEvent();

            Assert.That(copy.LongEventPending, Is.False);
            Assert.That(copy.Playing, Is.EqualTo(original.Playing));
            Assert.That(copy.WorldReady, Is.EqualTo(original.WorldReady));
            Assert.That(copy.Enabled, Is.EqualTo(original.Enabled));
            Assert.That(copy.CommitmentMode, Is.EqualTo(original.CommitmentMode));
            Assert.That(copy.ScribeActive, Is.EqualTo(original.ScribeActive));
            Assert.That(copy.VanillaSavingDisabled, Is.EqualTo(original.VanillaSavingDisabled));
            Assert.That(copy.MapTargeterActive, Is.EqualTo(original.MapTargeterActive));
            Assert.That(copy.WorldTargeterActive, Is.EqualTo(original.WorldTargeterActive));
            Assert.That(copy.RoutePlannerActive, Is.EqualTo(original.RoutePlannerActive));
            Assert.That(copy.ModalWindowOpen, Is.EqualTo(original.ModalWindowOpen));
            Assert.That(copy.FloatMenuOpen, Is.EqualTo(original.FloatMenuOpen));
        }

        [Test]
        public void IgnoringOwnLongEvent_DoesNotMutateTheOriginal()
        {
            var original = With(s => s.Conditions.LongEventPending = true);
            original.IgnoringOwnLongEvent();

            Assert.That(original.LongEventPending, Is.True);
        }

        [Test]
        public void IgnoringOwnLongEvent_UnblocksThatOneReason()
        {
            var conditions = With(s => s.Conditions.LongEventPending = true);

            Assert.That(ChronoSaveSchedule.FirstBlocker(conditions), Is.EqualTo(ChronoSaveBlocker.LongEventInFlight));
            Assert.That(ChronoSaveSchedule.FirstBlocker(conditions.IgnoringOwnLongEvent()), Is.EqualTo(ChronoSaveBlocker.None));
        }

        [Test]
        public void IgnoringOwnLongEvent_StillReportsEveryOtherBlocker()
        {
            // The one that stops someone "simplifying" the closure's re-check into a no-op. The
            // closure runs a frame or two after queueing, and a targeter or a dialog opening in that
            // window is exactly what it is there to catch.
            var conditions = With(s =>
            {
                s.Conditions.LongEventPending = true;
                s.Conditions.WorldTargeterActive = true;
            });

            Assert.That(ChronoSaveSchedule.FirstBlocker(conditions.IgnoringOwnLongEvent()),
                Is.EqualTo(ChronoSaveBlocker.WorldTargeting));
        }

        [Test]
        public void ShouldLogBlocker_SaysNothingWhenNothingIsBlocking()
        {
            Assert.That(ChronoSaveSchedule.ShouldLogBlocker(ChronoSaveBlocker.None, ChronoSaveBlocker.None), Is.False);
            Assert.That(ChronoSaveSchedule.ShouldLogBlocker(ChronoSaveBlocker.None, ChronoSaveBlocker.MapTargeting), Is.False);
        }

        [Test]
        public void ShouldLogBlocker_SaysNothingWhenTheReasonHasNotChanged()
        {
            // The frame update runs sixty times a second. Without the edge test this writes sixty
            // log lines a second for as long as a dialog is open.
            Assert.That(ChronoSaveSchedule.ShouldLogBlocker(ChronoSaveBlocker.MapTargeting, ChronoSaveBlocker.MapTargeting), Is.False);
        }

        [Test]
        public void ShouldLogBlocker_SpeaksWhenTheReasonChanges()
        {
            Assert.That(ChronoSaveSchedule.ShouldLogBlocker(ChronoSaveBlocker.MapTargeting, ChronoSaveBlocker.None), Is.True);
            Assert.That(ChronoSaveSchedule.ShouldLogBlocker(ChronoSaveBlocker.FloatMenuOpen, ChronoSaveBlocker.MapTargeting), Is.True);
        }

        [Test]
        public void IsDue_StaysTrueWhileTheSaveIsDeferred()
        {
            // Names the invariant the whole deferral rests on. A blocked frame must not write
            // lastSaveRealTime, and because it does not, the save goes out on the first frame the
            // state clears rather than an interval later. That is what the report asked for.
            Assert.That(ChronoSaveSchedule.IsDue(0f, 300f, 5f), Is.True);
            Assert.That(ChronoSaveSchedule.IsDue(0f, 3000f, 5f), Is.True);
        }

        private static Dictionary<string, ChronoSaveConditions> SingleFlagCases()
        {
            return new Dictionary<string, ChronoSaveConditions>
            {
                { "Playing = false", With(s => s.Conditions.Playing = false) },
                { "WorldReady = false", With(s => s.Conditions.WorldReady = false) },
                { "Enabled = false", With(s => s.Conditions.Enabled = false) },
                { "CommitmentMode", With(s => s.Conditions.CommitmentMode = true) },
                { "ScribeActive", With(s => s.Conditions.ScribeActive = true) },
                { "LongEventPending", With(s => s.Conditions.LongEventPending = true) },
                { "VanillaSavingDisabled", With(s => s.Conditions.VanillaSavingDisabled = true) },
                { "MapTargeterActive", With(s => s.Conditions.MapTargeterActive = true) },
                { "WorldTargeterActive", With(s => s.Conditions.WorldTargeterActive = true) },
                { "RoutePlannerActive", With(s => s.Conditions.RoutePlannerActive = true) },
                { "ModalWindowOpen", With(s => s.Conditions.ModalWindowOpen = true) },
                { "FloatMenuOpen", With(s => s.Conditions.FloatMenuOpen = true) },
            };
        }
    }
}
