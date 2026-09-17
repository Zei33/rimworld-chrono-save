using System;
using System.Linq;
using System.IO;
using ChronoSave.Core;
using NUnit.Framework;

namespace ChronoSave.Tests
{
    /// <summary>
    /// Covers <see cref="ChronoSaveFiles"/> against a real temporary directory.
    /// </summary>
    /// <remarks>
    /// <c>System.IO</c> is fully reachable in this harness, unlike anything under <c>Find</c> or
    /// <c>Current.Game</c>. That is the whole reason the filesystem questions are asked through a
    /// plain path here rather than through <c>Verse.GenFilePaths</c>: the method that runs in game
    /// is the method these tests exercise.
    /// </remarks>
    [TestFixture]
    public class ChronoSaveFilesTests
    {
        private string directory;

        [SetUp]
        public void CreateTemporaryDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "ChronoSaveTests-" + Path.GetRandomFileName());
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

        private string WriteFile(string name, int bytes)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, new byte[bytes]);
            return path;
        }

        [Test]
        public void MeasureSaveFile_ReturnsTheLengthOfAFileOnDisk()
        {
            Assert.That(ChronoSaveFiles.MeasureSaveFile(WriteFile("Chronosave-1.rws", 4096)), Is.EqualTo(4096L));
        }

        [Test]
        public void MeasureSaveFile_ReportsAnEmptyFileAsEmptyRatherThanMissing()
        {
            // Zero and "not there" are different failures and the caller treats both as implausible,
            // but conflating them here would hide a truncated-to-nothing write in the log detail.
            Assert.That(ChronoSaveFiles.MeasureSaveFile(WriteFile("Chronosave-2.rws", 0)), Is.EqualTo(0L));
        }

        [Test]
        public void MeasureSaveFile_ReturnsNoFileWhenThePathDoesNotExist()
        {
            var missing = Path.Combine(directory, "Chronosave-9.rws");

            Assert.That(ChronoSaveFiles.MeasureSaveFile(missing), Is.EqualTo(ChronoSaveFiles.NoFile));
        }

        [Test]
        public void MeasureSaveFile_ReturnsNoFileForADirectory()
        {
            Assert.That(ChronoSaveFiles.MeasureSaveFile(directory), Is.EqualTo(ChronoSaveFiles.NoFile));
        }

        [Test]
        public void MeasureSaveFile_ReturnsNoFileForANullOrEmptyPath()
        {
            Assert.That(ChronoSaveFiles.MeasureSaveFile(null), Is.EqualTo(ChronoSaveFiles.NoFile));
            Assert.That(ChronoSaveFiles.MeasureSaveFile(string.Empty), Is.EqualTo(ChronoSaveFiles.NoFile));
        }

        [Test]
        public void MeasureSaveFile_NeverThrows()
        {
            // It runs inside the queued long event, where a throw is caught by LongEventHandler,
            // which knows nothing about the mod's in-flight latch. Chronosaving would then stop for
            // the rest of the session with nothing but a stack trace to say why.
            Assert.That(() => ChronoSaveFiles.MeasureSaveFile("\0invalid"), Throws.Nothing);
            Assert.That(() => ChronoSaveFiles.MeasureSaveFile("   "), Throws.Nothing);
        }

        [Test]
        public void Snapshot_AMissingFolderIsEmpty()
        {
            Assert.That(ChronoSaveFiles.Snapshot(Path.Combine(directory, "nope")), Is.Empty);
            Assert.That(ChronoSaveFiles.Snapshot(null), Is.Empty);
        }

        [Test]
        public void Snapshot_AnEmptyFolderIsEmpty()
        {
            Assert.That(ChronoSaveFiles.Snapshot(directory), Is.Empty);
        }

        [Test]
        public void Snapshot_ListsSaveFilesByBaseName()
        {
            WriteFile("Chronosave-1.rws", 10);
            WriteFile("My Colony.rws", 10);

            var names = ChronoSaveFiles.Snapshot(directory).Select(s => s.Name).OrderBy(n => n).ToArray();

            Assert.That(names, Is.EqualTo(new[] { "Chronosave-1", "My Colony" }));
        }

        [Test]
        public void Snapshot_IgnoresSafeSaverWorkingFilesAndUnrelatedFiles()
        {
            // SafeSaver writes .new and moves the live file to .old, and both have an extension that
            // is not .rws, which is how vanilla excludes them too. A .rws.old counted as a save would
            // make the rotation think a slot is occupied when the load dialog cannot see it.
            WriteFile("Chronosave-1.rws", 10);
            WriteFile("Chronosave-2.rws.old", 10);
            WriteFile("Chronosave-3.rws.new", 10);
            WriteFile("notes.txt", 10);

            var names = ChronoSaveFiles.Snapshot(directory).Select(s => s.Name).ToArray();

            Assert.That(names, Is.EqualTo(new[] { "Chronosave-1" }));
        }

        [Test]
        public void Snapshot_MatchesTheExtensionCaseInsensitively()
        {
            // Vanilla's filter is ordinal, so a .RWS file is invisible to it. On a case-insensitive
            // filesystem that is the same file, and treating its slot as free would overwrite it.
            WriteFile("Chronosave-4.RWS", 10);

            Assert.That(ChronoSaveFiles.Snapshot(directory).Select(s => s.Name).ToArray(), Is.EqualTo(new[] { "Chronosave-4" }));
        }

        [Test]
        public void Snapshot_DoesNotRecurse()
        {
            Directory.CreateDirectory(Path.Combine(directory, "sub"));
            File.WriteAllBytes(Path.Combine(directory, "sub", "Chronosave-1.rws"), new byte[10]);

            Assert.That(ChronoSaveFiles.Snapshot(directory), Is.Empty);
        }

        [Test]
        public void Snapshot_ReportsTheLastWriteTimeInUtc()
        {
            // UTC rather than local, so an hour that happens twice under a daylight saving fall-back
            // cannot reorder the ring and make the rotation overwrite the newest file it has.
            var path = WriteFile("Chronosave-1.rws", 10);
            var written = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, written);

            var stamp = ChronoSaveFiles.Snapshot(directory).Single();

            Assert.That(stamp.LastWriteUtc, Is.EqualTo(written).Within(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void SnapshotFeedsChooseSlotOverARealFolder()
        {
            // The two halves joined up, because each is only useful if the names and times it
            // produces are the ones the other expects.
            foreach (var slot in new[] { 1, 2, 3 })
            {
                var path = WriteFile("Chronosave-Foo-" + slot + ".rws", 10);
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, slot, 0, 0, 0, DateTimeKind.Utc));
            }

            Assert.That(
                ChronoSaveSchedule.ChooseSlot("Foo", 3, ChronoSaveFiles.Snapshot(directory)),
                Is.EqualTo(1),
                "slot 1 is the oldest, so it is the one to reuse");

            File.Delete(Path.Combine(directory, "Chronosave-Foo-2.rws"));

            Assert.That(
                ChronoSaveSchedule.ChooseSlot("Foo", 3, ChronoSaveFiles.Snapshot(directory)),
                Is.EqualTo(2),
                "a free slot beats the oldest one");
        }

        [Test]
        public void MeasureSaveFile_ReadsTheCurrentLengthNotACachedOne()
        {
            // FileInfo caches its length from the first access, and this is called immediately
            // after the file it is measuring was rewritten. A cached length would verify the
            // previous save and pass a truncated one.
            var path = WriteFile("Chronosave-3.rws", 100);
            Assert.That(ChronoSaveFiles.MeasureSaveFile(path), Is.EqualTo(100L));

            File.WriteAllBytes(path, new byte[200]);

            Assert.That(ChronoSaveFiles.MeasureSaveFile(path), Is.EqualTo(200L));
        }
    }
}
