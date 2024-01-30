using System.Text.RegularExpressions;
using StdInMimic.IO;

namespace Pulsar.Backend.Data.ChunkProcessors;

/// <summary>
/// Regex-powered chunk processor for dynamic checking.
/// </summary>
/// <param name="Activate">RegEx to match conditionally whether to activate this segment (with a given group called `label' if specifying a label).</param>
/// <param name="Complete">RegEx to match conditionally whether to complete this segment.</param>
/// <param name="Update">RegEx to update labels for each segment where applicable (with a given group called `label' if specifying a label).</param>
/// <param name="Name">The name of this segment.</param>
/// <param name="Namespace">The namespace this segment operates in.</param>
public record struct RegexSegmentOptions(Regex Activate, Regex? Complete, Regex? Update, string Name,
    string? Namespace);

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
                string label = activationMatch.Groups["label"].Value;

                yield return manager.Activate(option.Name, option.Namespace,
                    !String.IsNullOrWhiteSpace(label) ? label : null);
            }
        }
    }

    public override IEnumerable<Segment> ProcessChunkProgress(Chunk chunk) {
        foreach (RegexSegmentOptions option in options) {
            if (option.Update == null) {
                Segment? segment = manager.UpdateActive(option.Name, chunk);
                if (segment != null) {
                    yield return segment;
                }
            }
            else {
                var updateMatches = option.Activate.Matches(chunk.Content);

                // Multiple label updates **can** occur. Why? No idea. Let's indulge it.
                foreach (Match updateMatch in updateMatches) {
                    string label = updateMatch.Groups["label"].Value;

                    Segment? segment = manager.UpdateActive(option.Name, chunk,
                        !String.IsNullOrWhiteSpace(label) ? label : null);
                    if (segment != null) {
                        yield return segment;
                    }
                }
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
}