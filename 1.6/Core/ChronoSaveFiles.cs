using System;
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
