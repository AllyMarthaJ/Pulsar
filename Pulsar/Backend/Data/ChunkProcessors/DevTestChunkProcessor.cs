using StdInMimic.IO;

namespace Pulsar.Backend.Data.ChunkProcessors;

public class DevTestChunkProcessor(SegmentManager manager) : ChunkProcessor(manager) {
    private static readonly string[] WATCHING_SEGMENTS =
        { "test-started", "test-succeed", "test-pending", "test-fail" };

    public override IEnumerable<Segment> CreatePossibleSegments() {
        return WATCHING_SEGMENTS
            .Select(manager.InstancePossibleSegment);
    }

    public override IEnumerable<Segment> ProcessActivation(Chunk chunk) {
        /*
         * This implementation is prone to something I'm calling Buffer Fracturing.
         * It's what happens when the standard input stream isn't read as expected;
         * if there is a delay in reading, then the input stream will get too far ahead
         * and more than expected characters will be read.
         *
         * In reality, regardless of how we read the standard input stream,
         * we should be resilient to unexpected groupings of logs.
         * Otherwise we may flakily miss some logs. e.g. 2 example started in one Chunk. 
         */
        if (chunk.Content.Contains("rspec >>> example started")) {
            // One can grep for /^example: (.*?)$/gm and simply return the first group
            // as the label. Just looking for basic impl right now.

            yield return manager.Activate("test-started", "test", null);
        }

        if (chunk.Content.Contains("rspec >>> example passed")) {
            // One can grep for /^example: (.*?)$/gm and simply return the first group
            // as the label. Just looking for basic impl right now.

            yield return manager.Activate("test-succeed", "test", null);
        }

        if (chunk.Content.Contains("rspec >>> example pending")) {
            // One can grep for /^example: (.*?)$/gm and simply return the first group
            // as the label. Just looking for basic impl right now.

            yield return manager.Activate("test-pending", "test", null);
        }

        if (chunk.Content.Contains("rspec >>> example failed")) {
            // One can grep for /^example: (.*?)$/gm and simply return the first group
            // as the label. Just looking for basic impl right now.

            yield return manager.Activate("test-fail", "test", null);
        }
    }

    public override IEnumerable<Segment> ProcessChunkProgress(Chunk chunk) {
        foreach (string watchingSegment in WATCHING_SEGMENTS) {
            Segment? segment = manager.UpdateActive(watchingSegment, chunk);
            if (segment != null) {
                yield return segment;
            }
        }
    }

    public override IEnumerable<Segment> ProcessCompletion(Chunk chunk) {
        // Each of these are transient one-off states!!
        // This means in each step they should be terminated as-is if they were matched in the same chunk.

        if (chunk.Content.Contains("rspec >>> example passed")) {
            // One can grep for /^example: (.*?)$/gm and simply return the first group
            // as the label. Just looking for basic impl right now.

            yield return manager.Complete("test-succeed");
        }

        if (chunk.Content.Contains("rspec >>> example pending")) {
            // One can grep for /^example: (.*?)$/gm and simply return the first group
            // as the label. Just looking for basic impl right now.

            yield return manager.Complete("test-pending");
        }

        if (chunk.Content.Contains("rspec >>> example failed")) {
            // One can grep for /^example: (.*?)$/gm and simply return the first group
            // as the label. Just looking for basic impl right now.

            yield return manager.Complete("test-fail");
        }
    }
}