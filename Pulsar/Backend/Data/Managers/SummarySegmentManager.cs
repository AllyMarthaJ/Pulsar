using System.Text;
using StdInMimic.IO;

namespace Pulsar.Backend.Data.Managers;

public class SummarySegmentManager : SegmentManager {
    private Dictionary<string, Segment> segments = new();

    public override Segment InstancePossibleSegment(string segmentName) {
        if (this.segments.ContainsKey(segmentName)) {
            throw new Exception("Can't instance an already instantiated segment");
        }
        
        var segment = new Segment(segmentName);
        segment.State.Push(new SegmentAction(SegmentState.NOT_YET, DateTime.Now, null));
        this.segments.Add(segmentName, segment);
        
        this.OnSegmentModified(new SegmentModifiedEventArgs(segment));

        return segment;
    }

    public override Segment Activate(string segmentName, string? ns, string? label) {
        Segment segment;
        if (!this.segments.TryGetValue(segmentName, out segment!)) {
            segment = new Segment(segmentName, ns);
            this.segments.Add(segmentName, segment);
        }

        // Consumption of events asynchronously means that this ts may not actually
        // correspond to the timestamp of the log itself; this is effectively
        // only the "client-side" timestamp.
        var action = new SegmentAction(SegmentState.STARTED, DateTime.Now, label);

        segment.State.Push(action);

        this.OnSegmentModified(new SegmentModifiedEventArgs(segment));

        // Always return a Segment.
        return segment;
    }

    public override Segment? UpdateActive(string segmentName, Chunk chunk) {
        if (!this.segments.TryGetValue(segmentName, out Segment? segment)) {
            throw new ArgumentException("Couldn't find requested segment", nameof(segmentName));
        }

        if (segment.GetCurrentState() != null && segment.GetCurrentState()?.State != SegmentState.STARTED) {
            return null;
        }

        segment.LogChunks.Add(chunk);

        this.OnSegmentModified(new SegmentModifiedEventArgs(segment));

        return segment;
    }

    public override Segment Complete(string segmentName, Chunk chunk) {
        if (!this.segments.TryGetValue(segmentName, out Segment? segment)) {
            throw new ArgumentException("Couldn't find requested segment", nameof(segmentName));
        }

        segment.LogChunks.Add(chunk);

        var action = new SegmentAction(SegmentState.ENDED, DateTime.Now, null);
        segment.State.Push(action);

        this.OnSegmentModified(new SegmentModifiedEventArgs(segment));

        return segment;
    }

    public override void Done() {
        DateTime now = DateTime.Now;

        foreach (var segment in this.segments.Values) {
            var curState = segment.GetCurrentState();

            SegmentState? newState = curState?.State switch {
                SegmentState.STARTED => SegmentState.ENDED,
                SegmentState.NOT_YET => SegmentState.NEVER_REACHED,
                _ => null
            };

            if (newState != null) {
                var action = new SegmentAction(newState.Value, now, null);
                segment.State.Push(action);
            }
            
            this.OnSegmentModified(new SegmentModifiedEventArgs(segment));
        }
    }

    public override string Dump(int maxLen = 100) {
        var sb = new StringBuilder();

        foreach (Segment segment in this.segments.Values) {
            var l =
                $"{segment.Name}: {segment.GetCurrentState()?.State} at {segment.GetCurrentState()?.Timestamp.ToLongTimeString()} [{segment.LogChunks.Count}]";
            sb.AppendLine(l + new string(' ', maxLen - l.Length));
        }

        return sb.ToString();
    }
}