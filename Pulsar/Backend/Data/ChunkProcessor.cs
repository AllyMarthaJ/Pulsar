using Pulsar.IO;

namespace Pulsar.Backend.Data;

/// <summary>
/// Stateful handlers of Chunks, acts as a go-between
/// for logs and segmentmanagers. Correspond 1:1 with namespaces,
/// though no-namespaces are permitted.
///
/// Very specific lifecycle methods are given to ChunkProcessors
/// to instantiatate 
/// </summary>
// TODO: Array of SegmentManager
public abstract class ChunkProcessor(SegmentManager manager) {
    public string? Namespace { get; }

    public abstract Segment[] ProcessActivation(Chunk chunk);

    public abstract Segment[] ProcessChunkProgress(Chunk chunk);

    public abstract Segment[] ProcessCompletion(Chunk chunk);

    public virtual Segment[] HandleCustom() => Array.Empty<Segment>();
}