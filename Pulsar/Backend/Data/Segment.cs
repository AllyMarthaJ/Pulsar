using StdInMimic.IO;

namespace Pulsar.Backend.Data;

public class Segment {
    /// <summary>
    /// Namespaces define groups of segment such that each segment's
    /// activity is mutually exclusive from one another.
    ///
    /// For example, a namespace might be "test", but segments "start",
    /// "succeed" and "fail" would be mutually exclusive in a single threaded
    /// environment.
    ///
    /// Implementation is such that namespaces provide behaviour-simplifying
    /// assumptions but segments can provide the granularity for task execution
    /// overview.
    /// </summary>
    public string? Namespace { get; set; }
    
    /// <summary>
    /// A segment name should uniquely (within a namespace) identify a process
    /// or series of processes. 
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// While the logs themselves can tell you when everything internally
    /// occurred, they won't answer the question "how many times did we start
    /// or stop logging this segment?" or "how much time overall did we consume
    /// in this segment?" or "how much time did we consume in the segment active state?"
    ///
    /// Each action can correspond locally to a distinct label corresponding to the
    /// state at the time. This allows sub-segment matching of logs to specific parameterise
    /// activity, such as tests; namespace of "test", the name might be "succeed",
    /// but which test exactly succeeded?
    ///
    /// SegmentActions should never be shared across segments.
    /// </summary>
    public Stack<SegmentAction> State { get; } = new();

    public List<Chunk> LogChunks { get; } = new();

    /// <summary>
    /// Fetch the current state (with label and such)
    /// </summary>
    /// <returns></returns>
    public SegmentAction? GetCurrentState() => this.State.Count > 0 ? this.State.Peek() : null;

    public Segment(string name, string? ns = null) {
        this.Name = name;
        this.Namespace = ns;
    }
}

public record struct SegmentAction(SegmentState State, DateTime Timestamp, string? Label);