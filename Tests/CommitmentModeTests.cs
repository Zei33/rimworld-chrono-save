using System.IO;
using ChronoSave.Core;
using NUnit.Framework;

namespace ChronoSave.Tests
{
    /// <summary>
    /// Covers the decisions behind refusing to chronosave a Commitment mode colony, and behind
    /// telling a player whose colony has already been bound into a chronosave slot.
    /// </summary>
    /// <remarks>
    /// The gate itself is one <c>if</c> over <c>Current.Game.Info.permadeathMode</c> and is out of
    /// reach here. What is testable, and what actually needed care, is deciding whether a given save
    /// name belongs to this mod's rotation. Getting that wrong in either direction is bad: too loose
    /// and the mod warns a player whose colony has nothing to do with it, too tight and it stays
    /// quiet while RimWorld writes a Commitment colony into a slot the mod recycles.
    /// </remarks>
    [TestFixture]
    public class CommitmentModeTests
    {
        [Test]
        public void IsRingSaveName_AcceptsTheLegacyFlatNames()
        {
            // Literal strings, not generated ones, and deliberately so. These are the names every
            // install wrote before the rotation was rewritten, and they have to keep being
            // recognised for as long as those files and the rebinds they caused exist on disk.
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave-1"), Is.True);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave-10"), Is.True);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave-25"), Is.True);
        }

        [Test]
        public void IsRingSaveName_RoundTripsTheGenerator()
        {
            // The parser and the generator have to change together. This fails if only one is edited.
            for (var slot = 1; slot <= ChronoSaveSchedule.MaxSlots; slot++)
            {
                Assert.That(ChronoSaveSchedule.IsRingSaveName(ChronoSaveSchedule.SaveNameForSlot(slot)), Is.True);

                foreach (var key in new[] { "Foo", "Crimson Fleet", "Kaldar's Hope", "X-2", "New Arrivals 3" })
                {
                    Assert.That(
                        ChronoSaveSchedule.IsRingSaveName(ChronoSaveSchedule.SaveNameForSlot(key, slot)),
                        Is.True,
                        key + " slot " + slot);
                }
            }
        }

        [Test]
        public void TryParseRingSlot_ReturnsTheSlot()
        {
            Assert.That(ChronoSaveSchedule.TryParseRingSlot("Chronosave-7", out var flat), Is.True);
            Assert.That(flat, Is.EqualTo(7));

            Assert.That(ChronoSaveSchedule.TryParseRingSlot("Chronosave-Crimson Fleet-12", out var keyed), Is.True);
            Assert.That(keyed, Is.EqualTo(12));
        }

        [Test]
        public void IsRingSaveName_IgnoresCase()
        {
            // Windows and default macOS filesystems are case-insensitive, so chronosave-3.rws is the
            // file Chronosave-3 resolves to.
            Assert.That(ChronoSaveSchedule.IsRingSaveName("chronosave-3"), Is.True);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("CHRONOSAVE-3"), Is.True);
        }

        [Test]
        public void IsRingSaveName_RejectsNullEmptyAndTheBarePrefix()
        {
            Assert.That(ChronoSaveSchedule.IsRingSaveName(null), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName(string.Empty), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("   "), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave"), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave-"), Is.False);
        }

        [Test]
        public void IsRingSaveName_RejectsSlotsOutsideTheHardLimit()
        {
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave-0"), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave-26"), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave--1"), Is.False);
        }

        [Test]
        public void IsRingSaveName_RejectsPaddedSlotsAndTrailingText()
        {
            // This mod never writes a padded slot, so a file named that way is the player's own.
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave-03"), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave-3a"), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave-3.rws"), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave- 3"), Is.False);
        }

        [Test]
        public void IsRingSaveName_RejectsAColonyLegitimatelyNamedAfterTheMod()
        {
            // The reason this is an exact-shape parser rather than a StartsWith. A Commitment colony
            // the player named "Chronosave-3" has the unique name "Chronosave-3 (Permadeath)",
            // because both vanilla generators end with AppendedPermadeathModeSuffix. A prefix test
            // would claim it and the mod would tell an innocent player their save is at risk.
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave-3 (Permadeath)"), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosave (Permadeath)"), Is.False);
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Chronosaves-3"), Is.False);
        }

        [Test]
        public void IsRingSaveName_RejectsVanillaAutosaveNames()
        {
            Assert.That(ChronoSaveSchedule.IsRingSaveName("Autosave-1"), Is.False);
        }

        [Test]
        public void NeedsRebindWarning_SaysNothingOutsideCommitmentMode()
        {
            Assert.That(ChronoSaveSchedule.NeedsRebindWarning(false, "Chronosave-3", null), Is.False);
        }

        [Test]
        public void NeedsRebindWarning_SaysNothingForAnOrdinaryCommitmentName()
        {
            Assert.That(ChronoSaveSchedule.NeedsRebindWarning(true, "Tribe of Kaldar (Permadeath)", null), Is.False);
        }

        [Test]
        public void NeedsRebindWarning_SaysNothingForAMissingName()
        {
            // A non-permadeath save omits the key entirely, so the field loads as null.
            Assert.That(ChronoSaveSchedule.NeedsRebindWarning(true, null, null), Is.False);
            Assert.That(ChronoSaveSchedule.NeedsRebindWarning(true, string.Empty, null), Is.False);
        }

        [Test]
        public void NeedsRebindWarning_FiresForASlotNameNeverWarnedAbout()
        {
            Assert.That(ChronoSaveSchedule.NeedsRebindWarning(true, "Chronosave-3", null), Is.True);
        }

        [Test]
        public void NeedsRebindWarning_DoesNotRepeatForTheSameName()
        {
            // The flag is scribed, so reloading an affected colony must not warn again every time.
            Assert.That(ChronoSaveSchedule.NeedsRebindWarning(true, "Chronosave-3", "Chronosave-3"), Is.False);
        }

        [Test]
        public void NeedsRebindWarning_FiresAgainForADifferentSlot()
        {
            // Keyed on the name rather than a boolean precisely so this case is still reported.
            Assert.That(ChronoSaveSchedule.NeedsRebindWarning(true, "Chronosave-5", "Chronosave-3"), Is.True);
        }

        [Test]
        public void ShouldRescanBackups_CountsImmediatelyOnTheFirstDraw()
        {
            Assert.That(ChronoSaveSchedule.ShouldRescanBackups(float.NegativeInfinity, 12.3f), Is.True);
        }

        [Test]
        public void ShouldRescanBackups_IsFalseInsideTheInterval()
        {
            // The settings window redraws every frame. Without this it lists a directory 60 times a
            // second for as long as the window is open.
            Assert.That(ChronoSaveSchedule.ShouldRescanBackups(100f, 100f + ChronoSaveSchedule.BackupRescanIntervalSeconds - 0.1f), Is.False);
        }

        [Test]
        public void ShouldRescanBackups_IsTrueOnTheBoundary()
        {
            Assert.That(ChronoSaveSchedule.ShouldRescanBackups(100f, 100f + ChronoSaveSchedule.BackupRescanIntervalSeconds), Is.True);
        }
    }

    /// <summary>
    /// Covers <see cref="StrandedBackups"/> against a real temporary directory.
    /// </summary>
    [TestFixture]
    public class StrandedBackupsTests
    {
        private string directory;

        [SetUp]
        public void CreateTemporaryDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "ChronoSaveBackups-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void RemoveTemporaryDirectory()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private void WriteFile(string name, int bytes)
        {
            File.WriteAllBytes(Path.Combine(directory, name), new byte[bytes]);
        }

        [Test]
        public void IsStrandedBackupFileName_AcceptsARingBackup()
        {
            Assert.That(StrandedBackups.IsStrandedBackupFileName("Chronosave-1.rws.old"), Is.True);
            Assert.That(StrandedBackups.IsStrandedBackupFileName("Chronosave-Foo-1.rws.old"), Is.True);
        }

        [Test]
        public void IsStrandedBackupFileName_RejectsAPlainSave()
        {
            Assert.That(StrandedBackups.IsStrandedBackupFileName("Chronosave-1.rws"), Is.False);
        }

        [Test]
        public void IsStrandedBackupFileName_RejectsSomebodyElsesBackup()
        {
            // The whole reason these are reported and never deleted: a .rws.old is also vanilla's own
            // one-generation safety copy, and only the ones whose base name is a chronosave slot can
            // be attributed to this mod at all.
            Assert.That(StrandedBackups.IsStrandedBackupFileName("Tribe of Kaldar (Permadeath).rws.old"), Is.False);
            Assert.That(StrandedBackups.IsStrandedBackupFileName("Autosave-1.rws.old"), Is.False);
        }

        [Test]
        public void IsStrandedBackupFileName_RejectsNullEmptyAndTheBareSuffix()
        {
            Assert.That(StrandedBackups.IsStrandedBackupFileName(null), Is.False);
            Assert.That(StrandedBackups.IsStrandedBackupFileName(string.Empty), Is.False);
            Assert.That(StrandedBackups.IsStrandedBackupFileName(".rws.old"), Is.False);
        }

        [Test]
        public void Scan_AMissingOrEmptyDirectoryReportsNothing()
        {
            Assert.That(StrandedBackups.Scan(Path.Combine(directory, "nope")).Count, Is.EqualTo(0));
            Assert.That(StrandedBackups.Scan(null).Count, Is.EqualTo(0));
            Assert.That(StrandedBackups.Scan(directory).Count, Is.EqualTo(0));
        }

        [Test]
        public void Scan_CountsAndSizesOnlyRingBackups()
        {
            WriteFile("Chronosave-1.rws.old", 100);
            WriteFile("Chronosave-2.rws.old", 250);
            WriteFile("Autosave-1.rws.old", 999);
            WriteFile("My Colony (Permadeath).rws.old", 999);
            WriteFile("Chronosave-1.rws", 999);
            WriteFile("Chronosave-3.rws.new", 999);

            var report = StrandedBackups.Scan(directory);

            Assert.That(report.Count, Is.EqualTo(2));
            Assert.That(report.TotalBytes, Is.EqualTo(350L));
        }

        [Test]
        public void Scan_LeavesOutTheLoadedColonysOwnBackup()
        {
            // That one is vanilla's live safety copy for the file it is still writing, not a
            // leftover, and deleting it would cost the player their only rollback generation.
            WriteFile("Chronosave-1.rws.old", 100);
            WriteFile("Chronosave-2.rws.old", 250);

            var report = StrandedBackups.Scan(directory, "Chronosave-2");

            Assert.That(report.Count, Is.EqualTo(1));
            Assert.That(report.TotalBytes, Is.EqualTo(100L));
        }

        [Test]
        public void Scan_MatchesTheSuffixCaseInsensitively()
        {
            WriteFile("Chronosave-4.RWS.OLD", 42);

            Assert.That(StrandedBackups.Scan(directory).Count, Is.EqualTo(1));
        }

        [Test]
        public void Report_ConvertsBytesToMegabytes()
        {
            var report = new StrandedBackupReport(2, 3L * 1024L * 1024L);

            Assert.That(report.TotalMegabytes, Is.EqualTo(3.0).Within(0.001));
            Assert.That(StrandedBackupReport.Empty.Count, Is.EqualTo(0));
            Assert.That(StrandedBackupReport.Empty.TotalBytes, Is.EqualTo(0L));
        }
    }
}
