using System;
using System.Collections.Generic;
using ChronoSave.Core;
using NUnit.Framework;

namespace ChronoSave.Tests
{
    /// <summary>
    /// Covers <see cref="ChronoSaveSchedule"/>, which holds every chronosave decision that can be
    /// stated over plain values. Nothing here touches game state, so these run in a bare test
    /// process.
    /// </summary>
    [TestFixture]
    public class ChronoSaveScheduleTests
    {
        private static readonly DateTime Base = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static SaveFileStamp Stamp(string name, int minutesOld)
        {
            return new SaveFileStamp(name, Base.AddMinutes(-minutesOld));
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
        public void SaveNameForSlot_WithoutAColonyIsTheNameTheModHasAlwaysWritten()
        {
            // Byte for byte the legacy name. This is what makes the Chronosave-N files already on a
            // subscriber's disk the shared pool rather than orphans nothing will ever touch again.
            Assert.That(ChronoSaveSchedule.SaveNameForSlot(1), Is.EqualTo("Chronosave-1"));
            Assert.That(ChronoSaveSchedule.SaveNameForSlot(25), Is.EqualTo("Chronosave-25"));
            Assert.That(ChronoSaveSchedule.SaveNameForSlot(null, 3), Is.EqualTo(ChronoSaveSchedule.SaveNameForSlot(3)));
        }

        [Test]
        public void SaveNameForSlot_WithAColonyPutsTheKeyBetweenThePrefixAndTheSlot()
        {
            Assert.That(ChronoSaveSchedule.SaveNameForSlot("Crimson Fleet", 3), Is.EqualTo("Chronosave-Crimson Fleet-3"));
        }

        [Test]
        public void SaveNameForSlot_KeysContainingDashesAndDigitsDoNotCollide()
        {
            // Names are only ever generated and compared, never parsed back apart, so the ambiguity
            // this looks like it should have does not exist.
            Assert.That(
                ChronoSaveSchedule.SaveNameForSlot("X-2", 1),
                Is.Not.EqualTo(ChronoSaveSchedule.SaveNameForSlot("X", 2)));
        }

        [Test]
        public void RingKey_AnUnnamedColonyUsesTheSharedPool()
        {
            // A colony has no name for at least its first 4.3 game days, because FactionGenerator
            // skips name generation for the player faction. Every throwaway start therefore shares
            // one set of files and leaks nothing.
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName(null), Is.Null);
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName(string.Empty), Is.Null);
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("   "), Is.Null);
        }

        [Test]
        public void RingKey_KeepsAnOrdinaryName()
        {
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("Crimson Fleet"), Is.EqualTo("Crimson Fleet"));
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("Kaldar's Hope"), Is.EqualTo("Kaldar's Hope"));
        }

        [Test]
        public void RingKey_KeepsNonLatinNames()
        {
            // Vanilla already writes faction names into filenames through
            // SaveGameFilesUtility.UnusedDefaultFileName, so this is not new ground.
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("Новая заря"), Is.EqualTo("Новая заря"));
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("黎明集落"), Is.EqualTo("黎明集落"));
        }

        [Test]
        public void RingKey_ReplacesFilesystemHostileCharactersWithAnUnderscore()
        {
            // Matches Verse.GenText.SanitizeFilename, which joins the surviving runs rather than
            // closing the gap, so two words stay two words.
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("Ridge/Hold"), Is.EqualTo("Ridge_Hold"));
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("A/B\\C:D*E?F\"G<H>I|J"), Is.EqualTo("A_B_C_D_E_F_G_H_I_J"));
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("Foo{}!@#$%^&Bar"), Is.EqualTo("Foo_Bar"));
        }

        [Test]
        public void RingKey_UsesAFixedCharacterSetNotTheHostPlatforms()
        {
            // Path.GetInvalidFileNameChars() on Mono under macOS is only the null character and the
            // forward slash. A colony name with a colon or a quote would produce a file Windows
            // cannot open, for a player who syncs saves or changes machine.
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("Nine:Tails"), Is.EqualTo("Nine_Tails"));
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("The \"Rock\""), Is.EqualTo("The _Rock"));
        }

        [Test]
        public void RingKey_StripsControlCharacters()
        {
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("Foo\u0001\u001fBar"), Is.EqualTo("Foo_Bar"));
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("Foo\u007fBar"), Is.EqualTo("Foo_Bar"));
        }

        [Test]
        public void RingKey_TrimsWhitespaceAndTrailingDots()
        {
            // Windows strips a trailing dot silently, which would make the name written differ from
            // the name looked for on the next pass.
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("  Foo.  "), Is.EqualTo("Foo"));
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("Foo..."), Is.EqualTo("Foo"));
        }

        [Test]
        public void RingKey_CapsTheLengthAndTidiesTheCut()
        {
            var key = ChronoSaveSchedule.RingKeyFromColonyName(new string('a', 64));

            Assert.That(key.Length, Is.EqualTo(ChronoSaveSchedule.MaxColonyKeyLength));
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName(new string('a', 39) + " tail"), Is.EqualTo(new string('a', 39)));
        }

        [Test]
        public void RingKey_ANameThatSanitisesAwayEntirelyUsesTheSharedPool()
        {
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("///"), Is.Null);
            Assert.That(ChronoSaveSchedule.RingKeyFromColonyName("..."), Is.Null);
        }

        [Test]
        public void ChooseSlot_AnEmptyFolderStartsAtSlotOne()
        {
            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 10, new List<SaveFileStamp>()), Is.EqualTo(1));
            Assert.That(ChronoSaveSchedule.ChooseSlot(null, 10, null), Is.EqualTo(1));
        }

        [Test]
        public void ChooseSlot_TakesTheFirstUnusedSlot()
        {
            var saves = new[] { Stamp("Chronosave-Foo-1", 10), Stamp("Chronosave-Foo-2", 5) };

            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 10, saves), Is.EqualTo(3));
        }

        [Test]
        public void ChooseSlot_FillsAGapBeforeOverwritingAnything()
        {
            // Slot 1 is by far the oldest, but slot 2 is free, and a free slot always wins. This is
            // RimWorld.Autosaver.NewAutosaveFileName's own order.
            var saves = new[] { Stamp("Chronosave-Foo-1", 999), Stamp("Chronosave-Foo-3", 5), Stamp("Chronosave-Foo-4", 1) };

            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 10, saves), Is.EqualTo(2));
        }

        [Test]
        public void ChooseSlot_WhenEverySlotIsUsedTakesTheOldest()
        {
            var saves = new[]
            {
                Stamp("Chronosave-Foo-1", 10),
                Stamp("Chronosave-Foo-2", 20),
                Stamp("Chronosave-Foo-3", 90),
                Stamp("Chronosave-Foo-4", 30),
                Stamp("Chronosave-Foo-5", 1),
            };

            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 5, saves), Is.EqualTo(3));
        }

        [Test]
        public void ChooseSlot_TiesGoToTheLowestSlot()
        {
            // Matches GenCollection.MinBy, whose comparison is a strict less-than, so the first
            // candidate wins a tie.
            var allEqual = new[]
            {
                Stamp("Chronosave-Foo-1", 10), Stamp("Chronosave-Foo-2", 10), Stamp("Chronosave-Foo-3", 10),
            };
            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 3, allEqual), Is.EqualTo(1));

            var twoTiedOldest = new[]
            {
                Stamp("Chronosave-Foo-1", 5), Stamp("Chronosave-Foo-2", 90),
                Stamp("Chronosave-Foo-3", 1), Stamp("Chronosave-Foo-4", 90),
            };
            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 4, twoTiedOldest), Is.EqualTo(2));
        }

        [Test]
        public void ChooseSlot_LoweringTheSlotCountIgnoresFilesAboveTheNewLimit()
        {
            // The player dropped the count from 10 to 3. Slot 7 is the oldest file in the folder and
            // must not be chosen; the answer comes from slots 1 to 3 only.
            var saves = new List<SaveFileStamp>();
            for (var slot = 1; slot <= 10; slot++)
            {
                saves.Add(Stamp("Chronosave-Foo-" + slot, slot == 7 ? 999 : slot));
            }

            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 3, saves), Is.EqualTo(3));
        }

        [Test]
        public void ChooseSlot_RaisingTheSlotCountUsesTheNewSlotFirst()
        {
            var saves = new[] { Stamp("Chronosave-Foo-1", 3), Stamp("Chronosave-Foo-2", 2), Stamp("Chronosave-Foo-3", 1) };

            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 10, saves), Is.EqualTo(4));
        }

        [Test]
        public void ChooseSlot_IgnoresOtherColoniesAndUnrelatedSaves()
        {
            // The whole point of scoping. Another colony's ring, the shared pool and the player's
            // own files are all invisible to this colony's rotation.
            var saves = new[]
            {
                Stamp("Chronosave-Other-1", 1),
                Stamp("Chronosave-1", 1),
                Stamp("Autosave-1", 1),
                Stamp("New Arrivals1", 1),
            };

            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 10, saves), Is.EqualTo(1));
            Assert.That(ChronoSaveSchedule.ChooseSlot(null, 10, saves), Is.EqualTo(2), "the pool should see Chronosave-1 and only that");
        }

        [Test]
        public void ChooseSlot_MatchesNamesCaseInsensitively()
        {
            // Vanilla's SavedGameNamedExists compares ordinally, so on a case-insensitive
            // filesystem it reports chronosave-1.rws as absent for Chronosave-1 and then writes
            // over it as if the slot were free.
            var saves = new[] { Stamp("chronosave-foo-1", 1) };

            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 10, saves), Is.EqualTo(2));
        }

        [Test]
        public void ChooseSlot_TreatsAFileWithNoReadableTimestampAsTheNewest()
        {
            // ChronoSaveFiles stamps an unreadable timestamp as DateTime.MaxValue. Choosing it for
            // overwrite would destroy the one file the mod understands least.
            var saves = new[]
            {
                new SaveFileStamp("Chronosave-Foo-1", DateTime.MaxValue),
                Stamp("Chronosave-Foo-2", 1),
            };

            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 2, saves), Is.EqualTo(2));
        }

        [Test]
        public void ChooseSlot_WhenNothingHasAReadableTimestampFallsBackToSlotOne()
        {
            var saves = new[]
            {
                new SaveFileStamp("Chronosave-Foo-1", DateTime.MaxValue),
                new SaveFileStamp("Chronosave-Foo-2", DateTime.MaxValue),
            };

            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 2, saves), Is.EqualTo(1));
        }

        [Test]
        public void ChooseSlot_DoesNotDependOnTheOrderTheFolderWasListedIn()
        {
            var saves = new List<SaveFileStamp>
            {
                Stamp("Chronosave-Foo-1", 10), Stamp("Chronosave-Foo-2", 90), Stamp("Chronosave-Foo-3", 1),
            };
            var reversed = new List<SaveFileStamp>(saves);
            reversed.Reverse();

            Assert.That(
                ChronoSaveSchedule.ChooseSlot("Foo", 3, reversed),
                Is.EqualTo(ChronoSaveSchedule.ChooseSlot("Foo", 3, saves)));
        }

        [Test]
        public void ChooseSlot_ANonPositiveSlotCountReturnsSlotOne()
        {
            // Vanilla throws here, MinBy refusing an empty sequence. The settings window clamps the
            // count to 1 through 25, so this is unreachable short of a hand-edited config.
            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 0, new List<SaveFileStamp>()), Is.EqualTo(1));
            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", -1, new List<SaveFileStamp>()), Is.EqualTo(1));
        }

        [Test]
        public void ChooseSlot_ASingleSlotRingAlwaysReturnsThatSlot()
        {
            Assert.That(ChronoSaveSchedule.ChooseSlot("Foo", 1, new[] { Stamp("Chronosave-Foo-1", 1) }), Is.EqualTo(1));
        }

        [Test]
        public void IsInRing_RecognisesEverySlotTheRingWouldWrite()
        {
            for (var slot = 1; slot <= 10; slot++)
            {
                Assert.That(ChronoSaveSchedule.IsInRing(ChronoSaveSchedule.SaveNameForSlot("Foo", slot), "Foo", 10), Is.True);
                Assert.That(ChronoSaveSchedule.IsInRing(ChronoSaveSchedule.SaveNameForSlot(null, slot), null, 10), Is.True);
            }
        }

        [Test]
        public void IsInRing_RejectsASlotAboveTheCurrentCount()
        {
            // The player lowered the count between a failed save and its retry, so the spoiled file
            // is no longer part of the ring and the retry has to choose a slot normally.
            Assert.That(ChronoSaveSchedule.IsInRing("Chronosave-Foo-7", "Foo", 3), Is.False);
        }

        [Test]
        public void IsInRing_RejectsAnotherColonysName()
        {
            // The colony gained a name between the failed save and the retry, which is the other way
            // a pending retry stops being valid.
            Assert.That(ChronoSaveSchedule.IsInRing("Chronosave-3", "Foo", 10), Is.False);
            Assert.That(ChronoSaveSchedule.IsInRing("Chronosave-Other-3", "Foo", 10), Is.False);
        }

        [Test]
        public void IsInRing_RejectsNullAndEmpty()
        {
            Assert.That(ChronoSaveSchedule.IsInRing(null, "Foo", 10), Is.False);
            Assert.That(ChronoSaveSchedule.IsInRing(string.Empty, "Foo", 10), Is.False);
        }

        [Test]
        public void IsInRing_MatchesCaseInsensitively()
        {
            Assert.That(ChronoSaveSchedule.IsInRing("chronosave-foo-3", "Foo", 10), Is.True);
        }
    }
}
