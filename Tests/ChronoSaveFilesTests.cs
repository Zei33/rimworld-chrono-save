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
