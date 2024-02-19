using System.Text;
using StdInMimic.IO;

namespace Pulsar.Backend.Data.Managers;

public class SummarySegmentManager : SegmentManager {
    private readonly Dictionary<string, Segment> segments = new();

    public override Segment InstancePossibleSegment(string segmentName) {
        if (this.segments.ContainsKey(segmentName)) {
            throw new Exception("Can't instance an already instantiated segment");
        }

        var segment = new Segment(segmentName);
        segment.State.AddLast(new SegmentAction(SegmentState.NOT_YET, DateTime.Now, null));
        this.segments.Add(segmentName, segment);

        this.OnSegmentModified(new SegmentModifiedEventArgs(segment));

        return segment;
    }

    public override Segment Activate(string segmentName, string? ns, string? label) {
        if (!this.segments.TryGetValue(segmentName, out Segment? segment)) {
            segment = new Segment(segmentName, ns);
            this.segments.Add(segmentName, segment);
        }
        else {
            // Only Activate if not already activated.
            SegmentAction? cs = segment.GetCurrentState();
            if (cs is { State: SegmentState.STARTED } && segment.GetLatestLabel() == label) {
                return segment;
            }
        }

        // Reassign namespace if required.
        segment.Namespace = ns;

        // Consumption of events asynchronously means that this ts may not actually
        // correspond to the timestamp of the log itself; this is effectively
        // only the "client-side" timestamp.
        var action = new SegmentAction(SegmentState.STARTED, DateTime.Now, label);

        segment.State.AddLast(action);

        this.OnSegmentModified(new SegmentModifiedEventArgs(segment));

        // Mark conflicting segments as complete
        if (!String.IsNullOrEmpty(ns)) {
            var conflictingSegments =
                this.segments.Where((g) => g.Value.Namespace == ns && g.Value.Name != segmentName);

            foreach ((string? _, Segment? conflictingSegment) in conflictingSegments) {
                this.Complete(conflictingSegment.Name);
            }
        }

        // Always return a Segment.
        return segment;
    }

    public override Segment? UpdateActive(string segmentName, Chunk chunk, string? label = null) {
        if (!this.segments.TryGetValue(segmentName, out Segment? segment)) {
            throw new ArgumentException("Couldn't find requested segment " + segmentName, nameof(segmentName));
        }

        var cs = segment.GetCurrentState();
        if (cs.HasValue && cs.Value.State != SegmentState.STARTED) {
            return null;
        }

        var doUpdate = false;

        if (label != null) {
            // This is a correction to the Segment's label
            // It's entirely possible it was missed in the activation stage.
            segment.State.AddLast(new SegmentAction(SegmentState.STARTED, DateTime.Now, label));
            doUpdate = true;
        }

        // Reference check to ensure we aren't double-appending chunks.
        // This can happen if there are multiple updates in a single chunk.
        if (segment.SumLogs() == 0 || segment.Last() != chunk) {
            segment.Push(chunk);
            doUpdate = true;
        }

        if (doUpdate) {
            this.OnSegmentModified(new SegmentModifiedEventArgs(segment));
        }

        return segment;
    }

    public override Segment Complete(string segmentName) {
        if (!this.segments.TryGetValue(segmentName, out Segment? segment)) {
            throw new ArgumentException("Couldn't find requested segment", nameof(segmentName));
        }

        // Only Complete if not already completed.
        SegmentAction? cs = segment.GetCurrentState();
        if (cs is { State: SegmentState.ENDED }) {
            return segment;
        }

        // Don't update LogChunks here -- it will have been updated in
        // the last round of UpdateActive. Double counting sucks.

        var action = new SegmentAction(SegmentState.ENDED, DateTime.Now, null);
        segment.State.AddLast(action);

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
                segment.State.AddLast(action);
            }

            this.OnSegmentModified(new SegmentModifiedEventArgs(segment));
        }
    }

    public override string Dump(int maxLen = 100) {
        var sb = new StringBuilder();

        foreach (Segment segment in this.segments.Values) {
            var l =
                $"{segment.Name}: {segment.GetCurrentState()?.State} at {segment.GetCurrentState()?.Timestamp.ToLongTimeString()} [{segment.SumLogs()}] [{segment.GetLatestLabel()}]";
            if (l.Length > maxLen) {
                l = l[0..(maxLen-3)] + "...";
            }
            sb.AppendLine(l + new string(' ', maxLen - l.Length));
        }

        return sb.ToString();
    }
}