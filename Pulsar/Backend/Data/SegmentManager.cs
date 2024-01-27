using Pulsar.IO;

namespace Pulsar.Backend.Data;

public class SegmentModifiedEventArgs(Segment segment) : EventArgs {
    public Segment Segment { get; } = segment;
}

public abstract class SegmentManager {
    public event EventHandler<SegmentModifiedEventArgs>? SegmentModified;

    protected virtual void OnSegmentModified(SegmentModifiedEventArgs e) {
        this.SegmentModified?.Invoke(this, e);
    }

    /// <summary>
    /// Creates a new segment instance with no namespace.
    /// Segments can change namespaces, but, importantly, possible segments
    /// don't yet have a state and won't respond to namespace changes. 
    /// </summary>
    /// <param name="segmentName"></param>
    /// <returns></returns>
    public abstract Segment InstancePossibleSegment(string segmentName);
    
    /// <summary>
    /// Activation procedure for a Segment acknowledges the existence
    /// of a new or pre-existing segment.
    /// Does not accept a Chunk; that should be done in UpdateActive.
    /// </summary>
    /// <param name="segmentName"></param>
    /// <param name="ns"></param>
    /// <param name="label"></param>
    public abstract Segment Activate(string segmentName, string? ns, string? label);

    public abstract Segment? UpdateActive(string segmentName, Chunk chunk);

    public abstract Segment Complete(string segmentName, Chunk chunk);

    public abstract void Done();

    public abstract string Dump(int maxLen = 100);
}