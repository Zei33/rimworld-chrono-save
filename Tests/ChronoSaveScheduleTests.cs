using ChronoSave.Core;
using NUnit.Framework;

namespace ChronoSave.Tests
{
    /// <summary>
    /// Covers <see cref="ChronoSaveSchedule"/>, which holds every chronosave decision that can be
    /// stated as arithmetic. Nothing here touches game state, so these run in a bare test process.
    /// </summary>
    [TestFixture]
    public class ChronoSaveScheduleTests
    {
        [Test]
        public void SaveNameForSlot_BuildsTheVanillaStyleName()
        {
            Assert.That(ChronoSaveSchedule.SaveNameForSlot(1), Is.EqualTo("Chronosave-1"));
            Assert.That(ChronoSaveSchedule.SaveNameForSlot(25), Is.EqualTo("Chronosave-25"));
        }

        [Test]
        public void IsDue_IsFalseBeforeTheIntervalElapses()
        {
            // 4 minutes into a 5 minute interval.
            Assert.That(ChronoSaveSchedule.IsDue(0f, 240f, 5f), Is.False);
        }

        [Test]
        public void IsDue_IsTrueExactlyOnTheBoundary()
        {
            // The comparison is >=, so the interval elapsing exactly is due, not one frame later.
            Assert.That(ChronoSaveSchedule.IsDue(0f, 300f, 5f), Is.True);
        }

        [Test]
        public void IsDue_IsTrueAfterTheIntervalElapses()
        {
            Assert.That(ChronoSaveSchedule.IsDue(1000f, 1301f, 5f), Is.True);
        }

        [Test]
        public void IsDue_MeasuresFromTheLastSaveNotFromZero()
        {
            // A long session must not make every frame due.
            Assert.That(ChronoSaveSchedule.IsDue(10000f, 10100f, 5f), Is.False);
        }

        [Test]
        public void SlotInRange_LeavesASlotInsideTheConfiguredRangeAlone()
        {
            Assert.That(ChronoSaveSchedule.SlotInRange(3, 10), Is.EqualTo(3));
            Assert.That(ChronoSaveSchedule.SlotInRange(10, 10), Is.EqualTo(10));
        }

        [Test]
        public void SlotInRange_WrapsASlotAboveTheConfiguredRange()
        {
            // The player lowered NumberOfSaves from 10 to 3 while sitting on slot 7.
            Assert.That(ChronoSaveSchedule.SlotInRange(7, 3), Is.EqualTo(1));
        }

        [Test]
        public void SlotInRange_LeavesTheSlotAloneWhenNoSavesAreConfigured()
        {
            // Preserves what the loop this replaced did: its body never ran, so it returned the
            // slot unchanged. Settings clamp NumberOfSaves to 1..25, so this is unreachable in
            // practice and is pinned here only so the extraction stays faithful.
            Assert.That(ChronoSaveSchedule.SlotInRange(7, 0), Is.EqualTo(7));
        }

        [Test]
        public void AdvanceSlot_StepsThroughTheRing()
        {
            Assert.That(ChronoSaveSchedule.AdvanceSlot(1, 10), Is.EqualTo(2));
            Assert.That(ChronoSaveSchedule.AdvanceSlot(9, 10), Is.EqualTo(10));
        }

        [Test]
        public void AdvanceSlot_WrapsAtTheConfiguredMaximum()
        {
            Assert.That(ChronoSaveSchedule.AdvanceSlot(10, 10), Is.EqualTo(1));
        }

        [Test]
        public void AdvanceSlot_WithOneSlotAlwaysReturnsTheSameSlot()
        {
            Assert.That(ChronoSaveSchedule.AdvanceSlot(1, 1), Is.EqualTo(1));
        }

        [Test]
        public void SanitiseLoadedSlot_AcceptsEverySlotInsideTheHardLimit()
        {
            Assert.That(ChronoSaveSchedule.SanitiseLoadedSlot(1), Is.EqualTo(1));
            Assert.That(ChronoSaveSchedule.SanitiseLoadedSlot(25), Is.EqualTo(25));
        }

        [Test]
        public void SanitiseLoadedSlot_ResetsValuesOutsideTheHardLimit()
        {
            Assert.That(ChronoSaveSchedule.SanitiseLoadedSlot(0), Is.EqualTo(1));
            Assert.That(ChronoSaveSchedule.SanitiseLoadedSlot(-4), Is.EqualTo(1));
            Assert.That(ChronoSaveSchedule.SanitiseLoadedSlot(26), Is.EqualTo(1));
        }

        [Test]
        public void SanitiseLoadedSlot_IsBoundedByTheHardLimitNotByTheConfiguredCount()
        {
            // Slot 20 loaded while the player has NumberOfSaves at 5 survives this call. It is
            // SlotInRange, later, that brings it back down. Pinned because it is the behaviour the
            // rotation fix in chrono-save#1 has to replace deliberately rather than by accident.
            Assert.That(ChronoSaveSchedule.SanitiseLoadedSlot(20), Is.EqualTo(20));
        }
    }
}
