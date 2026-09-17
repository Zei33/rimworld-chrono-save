using System;

namespace ChronoSave.Core
{
    /// <summary>
    /// A save file as the rotation sees it: the base name, without directory or extension, and when
    /// it was last written.
    /// </summary>
    /// <remarks>
    /// The rotation needs nothing else about a file, so this is what crosses the boundary between
    /// <see cref="ChronoSaveFiles"/>, which touches the disk, and <see cref="ChronoSaveSchedule"/>,
    /// which decides. A test supplies these directly; the game supplies them from a real folder.
    /// </remarks>
    public readonly struct SaveFileStamp
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="SaveFileStamp"/> struct.
        /// </summary>
        /// <param name="name">The base name, with no directory and no extension.</param>
        /// <param name="lastWriteUtc">When the file was last written, in UTC.</param>
        public SaveFileStamp(string name, DateTime lastWriteUtc)
        {
            Name = name;
            LastWriteUtc = lastWriteUtc;
        }

        /// <summary>
        /// Gets the base name, with no directory and no extension.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets when the file was last written, in UTC.
        /// </summary>
        /// <remarks>
        /// UTC rather than local time, so an hour that happens twice under a daylight saving
        /// fall-back cannot reorder the ring and make the mod overwrite the newest file it has.
        /// </remarks>
        public DateTime LastWriteUtc { get; }
    }
}
