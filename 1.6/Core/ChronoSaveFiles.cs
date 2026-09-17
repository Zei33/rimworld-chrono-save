using System;
using System.Collections.Generic;
using System.IO;

namespace ChronoSave.Core
{
    /// <summary>
    /// The filesystem questions chronosaving asks, kept in one place and expressed over plain paths
    /// so a test process can answer them against a temporary directory.
    /// </summary>
    /// <remarks>
    /// Nothing here calls <c>Verse.GenFilePaths</c>. The caller resolves the path, which is the only
    /// reason any of this is reachable outside a running game.
    /// </remarks>
    public static class ChronoSaveFiles
    {
        /// <summary>
        /// The value <see cref="MeasureSaveFile"/> returns when there is nothing to measure.
        /// </summary>
        public const long NoFile = -1L;

        /// <summary>
        /// The extension RimWorld gives a save file.
        /// </summary>
        public const string SaveExtension = ".rws";

        /// <summary>
        /// Lists the save files in a folder.
        /// </summary>
        /// <param name="folderPath">The folder to list, normally RimWorld's Saves folder.</param>
        /// <returns>
        /// One <see cref="SaveFileStamp"/> per save file, in no particular order. A folder that does
        /// not exist is empty.
        /// </returns>
        /// <remarks>
        /// Not recursive, matching <c>Verse.GenFilePaths.AllSavedGameFiles</c>. The extension test
        /// is case-insensitive, unlike vanilla's, so a <c>.RWS</c> file is not mistaken for a free
        /// slot on a filesystem where it is the same file. <c>SafeSaver</c>'s working files are
        /// excluded for free, because their extension is <c>.new</c> or <c>.old</c>.
        ///
        /// A file whose timestamp cannot be read is stamped <see cref="DateTime.MaxValue"/> and so
        /// is treated as the newest thing in the folder, which keeps the rotation from choosing it
        /// for overwrite while any dated alternative exists. Failing to list the folder at all is
        /// allowed to propagate: if it cannot be read, the save about to be written to it is not
        /// going to succeed either, and the caller treats that as a failed attempt rather than
        /// silently writing to slot 1.
        /// </remarks>
        public static List<SaveFileStamp> Snapshot(string folderPath)
        {
            var stamps = new List<SaveFileStamp>();

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return stamps;
            }

            foreach (var file in new DirectoryInfo(folderPath).GetFiles())
            {
                if (!string.Equals(file.Extension, SaveExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTime writtenAt;
                try
                {
                    writtenAt = file.LastWriteTimeUtc;
                }
                catch (IOException)
                {
                    writtenAt = DateTime.MaxValue;
                }
                catch (UnauthorizedAccessException)
                {
                    writtenAt = DateTime.MaxValue;
                }

                stamps.Add(new SaveFileStamp(Path.GetFileNameWithoutExtension(file.Name), writtenAt));
            }

            return stamps;
        }

        /// <summary>
        /// Reads the current length of a written save file.
        /// </summary>
        /// <param name="absolutePath">Full path to the file.</param>
        /// <returns>
        /// The length in bytes, or <see cref="NoFile"/> when the path is empty, names something that
        /// is not a file, or cannot be read.
        /// </returns>
        /// <remarks>
        /// One stat call, not a read. A fresh <see cref="FileInfo"/> every time, deliberately:
        /// <see cref="FileInfo"/> caches its length from the first access, and this is called
        /// immediately after the file was rewritten.
        ///
        /// Never throws. It runs inside the queued long event, where an exception would be caught by
        /// <c>LongEventHandler</c>, which knows nothing about the mod's in-flight latch.
        /// </remarks>
        public static long MeasureSaveFile(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return NoFile;
            }

            try
            {
                var info = new FileInfo(absolutePath);
                if (!info.Exists)
                {
                    return NoFile;
                }

                return info.Length;
            }
            catch (IOException)
            {
                return NoFile;
            }
            catch (UnauthorizedAccessException)
            {
                return NoFile;
            }
            catch (ArgumentException)
            {
                return NoFile;
            }
            catch (NotSupportedException)
            {
                return NoFile;
            }
        }
    }
}
