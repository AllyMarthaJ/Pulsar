using System.Text.Json;
using System.Text.RegularExpressions;
using StdInMimic.IO;

namespace Pulsar.Backend.Data.ChunkProcessors;

/// <summary>
/// Regex-powered chunk processor for dynamic checking.
/// </summary>
/// <param name="Activate">RegEx to match conditionally whether to activate this segment (with a given group called `label' if specifying a label).</param>
/// <param name="Complete">RegEx to match conditionally whether to complete this segment.</param>
/// <param name="Name">The name of this segment.</param>
/// <param name="Namespace">The namespace this segment operates in.</param>
/// <param name="Summarise">RegEx which when matched yields a group `label' to summarise the active segment.</param>
public record struct RegexSegmentOptions(Regex Activate, Regex? Complete, string Name,
    string? Namespace, Regex? Summarise = null);

public class RegExChunkProcessor(
    SegmentManager manager,
    IEnumerable<RegexSegmentOptions> options) : ChunkProcessor(manager) {
    public override IEnumerable<Segment> CreatePossibleSegments() {
        return options.Select(option => manager.InstancePossibleSegment(option.Name));
    }

    public override IEnumerable<Segment> ProcessActivation(Chunk chunk) {
        foreach (var option in options) {
            MatchCollection activationMatches = option.Activate.Matches(chunk.Content);

            foreach (Match activationMatch in activationMatches) {
                yield return manager.Activate(option.Name, option.Namespace, null);
            }
        }
    }

    public override IEnumerable<Segment> ProcessChunkProgress(Chunk chunk) {
        foreach (RegexSegmentOptions option in options) {
            Segment? segment = manager.UpdateActive(option.Name, chunk);
            if (segment != null) {
                yield return segment;
            }
        }
    }

    public override IEnumerable<Segment> ProcessCompletion(Chunk chunk) {
        foreach (var option in options) {
            if (option.Complete == null) {
                continue;
            }

            MatchCollection completionMatches = option.Complete.Matches(chunk.Content);

            foreach (Match _ in completionMatches) {
                yield return manager.Complete(option.Name);
            }
        }
    }

    public override Segment? SummariseSegment(Segment segment) {
        RegexSegmentOptions? option = options
            .FirstOrDefault(option => 
                option.Name == segment.Name);

        var label = option?.Summarise?
            .Matches(segment
                .AggregatedLogs())
            .Select(match => match.Groups["label"])
            .LastOrDefault();
    
        if (label is null) {
            return null;
        }

        if (segment.GetLatestLabel() == label.Value) {
            return null;
        }
        return manager.UpdateActive(segment.Name, segment.Last(), label.Value);
    }
}