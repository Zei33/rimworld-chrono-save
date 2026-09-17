using System;
using System.IO;

namespace ChronoSave.Core
{
    /// <summary>
    /// A count and byte total of the backup copies RimWorld left behind beside chronosave slots.
    /// </summary>
    public readonly struct StrandedBackupReport
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="StrandedBackupReport"/> struct.
        /// </summary>
        /// <param name="count">How many files were found.</param>
        /// <param name="totalBytes">Their combined size.</param>
        public StrandedBackupReport(int count, long totalBytes)
        {
            Count = count;
            TotalBytes = totalBytes;
        }

        /// <summary>
        /// Gets how many leftover backup files were found.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Gets their combined size in bytes.
        /// </summary>
        public long TotalBytes { get; }

        /// <summary>
        /// Gets their combined size in megabytes, for display.
        /// </summary>
        public double TotalMegabytes => TotalBytes / (1024.0 * 1024.0);

        /// <summary>
        /// Gets a report of nothing found.
        /// </summary>
        public static StrandedBackupReport Empty => default(StrandedBackupReport);
    }

    /// <summary>
    /// Finds the backup copies RimWorld leaves beside chronosave slots in Commitment mode.
    /// </summary>
    /// <remarks>
    /// <c>GameDataSaveLoader.SaveGame</c> passes <c>Find.GameInfo.permadeathMode</c> as
    /// <c>SafeSaver.Save</c>'s <c>leaveOldFile</c>, so in Commitment mode every write moves the
    /// previous file to <c>.rws.old</c> and leaves it there. <c>SafeSaver</c> only clears that copy
    /// on the next write to the same path, and now that the mod refuses to write in Commitment mode
    /// there is no next write, so they sit on disk indefinitely. They are invisible in the load
    /// dialog, because <c>GenFilePaths.AllSavedGameFiles</c> filters on the <c>.rws</c> extension.
    ///
    /// **These are counted and never deleted.** A file of this shape can also be vanilla's own
    /// one-generation safety copy for a colony that has nothing to do with this mod, and there is no
    /// way to tell which is which from disk for any colony other than the one currently loaded.
    /// </remarks>
    public static class StrandedBackups
    {
        /// <summary>
        /// The suffix RimWorld gives the copy it keeps of a file it is about to replace.
        /// </summary>
        public const string BackupSuffix = ".rws.old";

        /// <summary>
        /// Decides from a filename alone whether it is a leftover chronosave slot backup.
        /// </summary>
        /// <param name="fileName">The filename, with its extensions and no directory.</param>
        /// <param name="excludeSaveName">
        /// A save name to leave out, normally the loaded Commitment colony's own, whose backup is
        /// vanilla's live safety copy rather than a leftover.
        /// </param>
        /// <returns><c>true</c> when the file is a leftover chronosave backup.</returns>
        public static bool IsStrandedBackupFileName(string fileName, string excludeSaveName = null)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.Length <= BackupSuffix.Length)
            {
                return false;
            }

            if (!fileName.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var saveName = fileName.Substring(0, fileName.Length - BackupSuffix.Length);

            if (!ChronoSaveSchedule.IsRingSaveName(saveName))
            {
                return false;
            }

            return !string.Equals(saveName, excludeSaveName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Counts the leftover chronosave backups in a folder.
        /// </summary>
        /// <param name="savesDirectory">The folder to scan, normally RimWorld's Saves folder.</param>
        /// <param name="excludeSaveName">A save name to leave out; see the other overload.</param>
        /// <returns>What was found, or <see cref="StrandedBackupReport.Empty"/> on any problem.</returns>
        /// <remarks>
        /// The suffix test is applied in code rather than as a <c>"*.rws.old"</c> search pattern,
        /// because Mono's pattern matching handles a two-dot pattern inconsistently across platforms.
        /// Anything that goes wrong reports nothing found: this is a disclosure in a settings window,
        /// and it must never throw out of a window's draw method.
        /// </remarks>
        public static StrandedBackupReport Scan(string savesDirectory, string excludeSaveName = null)
        {
            if (string.IsNullOrEmpty(savesDirectory) || !Directory.Exists(savesDirectory))
            {
                return StrandedBackupReport.Empty;
            }

            var count = 0;
            var totalBytes = 0L;

            try
            {
                foreach (var file in new DirectoryInfo(savesDirectory).GetFiles())
                {
                    if (!IsStrandedBackupFileName(file.Name, excludeSaveName))
                    {
                        continue;
                    }

                    count++;

                    try
                    {
                        totalBytes += file.Length;
                    }
                    catch (IOException)
                    {
                        // Counted but not sized. A total that is a little low is better than no
                        // report at all.
                    }
                }
            }
            catch (IOException)
            {
                return StrandedBackupReport.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return StrandedBackupReport.Empty;
            }

            return new StrandedBackupReport(count, totalBytes);
        }
    }
}
