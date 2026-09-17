namespace ChronoSave.Core
{
    /// <summary>
    /// What a chronosave attempt actually did, once the queued long event has run.
    /// </summary>
    /// <remarks>
    /// The mod cannot learn this from an exception. <c>Verse.GameDataSaveLoader.SaveGame</c> returns
    /// <c>void</c> and wraps everything in <c>catch { Log.Error(...) }</c>, and below it
    /// <c>Scribe_Deep.Look</c> catches anything short of an <c>OutOfMemoryException</c> thrown inside
    /// <c>ExposeData</c> and carries on, so a save that fails part way through still reaches
    /// <c>SafeSaver.FinalizeSaving</c> and commits a well formed but truncated document. The outcome
    /// therefore has to be established by looking at what landed on disk.
    /// </remarks>
    public enum ChronoSaveOutcome
    {
        /// <summary>
        /// A guard inside the queued event stopped the attempt before anything was written.
        /// </summary>
        Aborted,

        /// <summary>
        /// The write ran, but what ended up on disk did not look like a complete save.
        /// </summary>
        Failed,

        /// <summary>
        /// The write ran and what landed on disk verified.
        /// </summary>
        Succeeded
    }

    /// <summary>
    /// The consequences of one finished chronosave attempt, expressed as plain values so the
    /// decision can be made and tested without a running game.
    /// </summary>
    public readonly struct ChronoSaveResolution
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="ChronoSaveResolution"/> struct.
        /// </summary>
        /// <param name="outcome">What the attempt did.</param>
        /// <param name="lastSaveRealTime">The value to store as the last chronosave real time.</param>
        /// <param name="showSuccessMessage">Whether to tell the player the chronosave was written.</param>
        /// <param name="showFailureMessage">Whether to warn the player the chronosave may be incomplete.</param>
        public ChronoSaveResolution(ChronoSaveOutcome outcome, float lastSaveRealTime, bool showSuccessMessage, bool showFailureMessage)
        {
            Outcome = outcome;
            LastSaveRealTime = lastSaveRealTime;
            ShowSuccessMessage = showSuccessMessage;
            ShowFailureMessage = showFailureMessage;
        }

        /// <summary>
        /// Gets what the attempt actually did.
        /// </summary>
        public ChronoSaveOutcome Outcome { get; }

        /// <summary>
        /// Gets the value the component should store as its last chronosave real time.
        /// </summary>
        /// <remarks>
        /// Never left untouched. Every terminal outcome writes this, because the queued event
        /// clears the in-flight latch as it finishes and the frame update runs again immediately
        /// afterwards. A non-success outcome that left the timer alone would still satisfy
        /// <see cref="ChronoSaveSchedule.IsDue"/> and the whole cycle would repeat every few frames.
        /// </remarks>
        public float LastSaveRealTime { get; }

        /// <summary>
        /// Gets whether to tell the player the chronosave was written.
        /// </summary>
        public bool ShowSuccessMessage { get; }

        /// <summary>
        /// Gets whether to warn the player the chronosave may be incomplete.
        /// </summary>
        public bool ShowFailureMessage { get; }
    }
}
