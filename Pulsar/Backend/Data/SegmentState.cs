namespace Pulsar.Backend.Data;

public enum SegmentState {
    /// <summary>
    /// The segment has been instantiated, or prepared for,
    /// but was never reached. 
    /// </summary>
    NOT_YET,
    /// <summary>
    /// The segment has been instantiated and logs have been
    /// provided to open this segment.
    /// </summary>
    STARTED,
    /// <summary>
    /// The segment has been started, and logs have been provided
    /// to close this segment, or the stream was terminated.
    /// </summary>
    ENDED,
    /// <summary>
    /// The segment was instantiated, but never started. Either it
    /// is an irrelevant segment, or there is a bug somewhere.
    /// </summary>
    NEVER_REACHED,
}