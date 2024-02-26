using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Pulsar.Backend.Data;
using Pulsar.Backend.Data.ChunkProcessors;
using Pulsar.Backend.Data.Managers;
using StdInMimic.IO;

namespace Pulsar {
    internal class Program {
        private const int MAX_LEN = 230;
        private const bool DO_POST_PROCESSING = true;

        const string ESC = "\x1b[";
        const string ZERO = ESC + "H";

        // TODO: Array of SegmentManager
        private static SegmentManager manager = new SummarySegmentManager();

        private static ChunkProcessor[] registeredProcessors = {
            new RegExChunkProcessor(manager, new RegexSegmentOptions[] {
                new RegexSegmentOptions(
                    new Regex("I,.*?rspec >>> example started"),
                    null,
                    "test-started",
                    "test",
                    new Regex("example: (?<label>.*)\\n?")
                ),
                new RegexSegmentOptions(
                    new Regex("I,.*?rspec >>> example passed"),
                    new Regex("I,.*?rspec >>> example passed"),
                    "test-succeed",
                    "test",
                    new Regex("example: (?<label>.*)\\n?")
                ),
                new RegexSegmentOptions(new Regex(
                        "I,.*?rspec >>> example failed"),
                    new Regex("I,.*?rspec >>> example failed"),
                    "test-failed",
                    "test",
                    new Regex("example: (?<label>.*)\\n?")
                ),
            }),
            new RegExChunkProcessor(manager, new RegexSegmentOptions[] {
                new RegexSegmentOptions(
                    new Regex("^\\s+\u2714 .*", RegexOptions.Multiline),
                    new Regex("^\\s+\u2714 .*", RegexOptions.Multiline),
                    "test-succeeded",
                    "karma-test",
                    new Regex("^\\s+\u2714 (?<label>.*)", RegexOptions.Multiline))
            })
        };

        private static Dictionary<string, ChunkProcessor[]> chunkProcessorsByNamespace = registeredProcessors
            .GroupBy((ac) => ac.Namespace)
            .ToDictionary((g) => String.IsNullOrEmpty(g.Key) ? "--reserved--" : g.Key, g => g.ToArray());

        public static async Task Main(string[] args) {
            HashSet<Segment> segmentsToProcess = new();

            manager.SegmentModified += (sender, ev) => {
                if (DO_POST_PROCESSING) {
                    segmentsToProcess.Add(ev.Segment);
                }

                // TODO: UI consumers of this event should 
                // do everything on a separate thread
                var dump = manager.Dump(MAX_LEN).Split("\n");

                for (int i = 0; i < dump.Length; i++) {
                    var line = dump[i];
                    Console.WriteLine($"{ESC}{i + 2};0H" + line);
                    // Console.WriteLine(line);
                }
            };

            // STDIN is a long-running unbounded stream so I guess it makes
            // sense to replicate that here. We could/should probably 
            // bound this so we don't accidentally yeet ourselves if we don't
            // consume enough fast enough??
            // What should happen if we drop messages anyway...
            Channel<Chunk> chunkChannel = Channel.CreateUnbounded<Chunk>();
            var chunkReader = chunkChannel.Reader;

            var command = new Args(args);
            TextReader reader = Console.In;
            if (command.ShellCommand.Length > 0) {
                // Start the specified process and redirect the output stream
                // to pseudo StdIn.
                var psi = new ProcessStartInfo(
                        // Explode if this isn't available. We can't execute anything
                        // if it's not, so....
                        Environment.GetEnvironmentVariable("SHELL")!,
                        // Both zsh and bash support -c as a subcommand to execute
                        // anything past this point.
                        // Also add this stupid hack to redirect stderr to stdout.
                        // Ew. TODO: Multistream listener for log listening.
                        new[] { "-c", String.Join(" ", command.ShellCommand) + " 2>&1" } 
                    ) {
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                    };

                var proc = Process.Start(psi);
                if (proc is null) {
                    Console.WriteLine("Failed to capture process, defaulting to StdIn instead.");
                }
                else {
                    reader = proc.StandardOutput;
                }
            }

            StdinListener listener = new(reader, chunkChannel,
                args.Length > 0 && args[0].Equals("greedy", StringComparison.InvariantCultureIgnoreCase)
                    ? StdinListenerFlags.GREEDY
                    : StdinListenerFlags.NONE);

            AppDomain.CurrentDomain.ProcessExit += (o, e) => { Console.WriteLine("Stopping listener..."); };

            listener.Start();

            // --- Create Segment instances ---
            Console.WriteLine("Creating instances");
            Dictionary<string, Segment[]> initSegmentsByNamespace = chunkProcessorsByNamespace
                .ToDictionary(
                    g => g.Key,
                    g => g.Value.SelectMany(processor => processor.CreatePossibleSegments()).ToArray()
                );

            while (await chunkReader.WaitToReadAsync()) {
                Chunk? lastChunk = null;
                while (chunkReader.TryRead(out Chunk? chunk)) {
                    lastChunk = chunk;

                    // On chunk read, each registered log processor should,
                    // given an active manager, call various methods.

                    // --- Activation stage ---

                    Dictionary<string, Segment[]> activatedSegmentsByNamespace =
                        chunkProcessorsByNamespace
                            .ToDictionary(
                                g => g.Key,
                                g => g.Value.SelectMany(processor => processor.ProcessActivation(chunk)).ToArray()
                            )
                            .Where((g) => !String.IsNullOrEmpty(g.Key))
                            .ToDictionary();

                    // The SegmentManager should be responsible for managing the deactivation
                    // of conflicting segments in each namespace. However, multiple activations 
                    // are not allowed to occur in the same namespace simultaneously.

                    bool hasConflictingActivations = activatedSegmentsByNamespace
                        .Any((kv) => kv.Value.Length > 1);
                    if (hasConflictingActivations) {
                        throw new Exception("Multiple conflicting segments activated");
                    }

                    // --- Progress stage ---

                    Dictionary<string, Segment[]> progressSegmentsByNamespace = chunkProcessorsByNamespace
                        .ToDictionary(
                            g => g.Key,
                            g => g.Value.SelectMany(processor => processor.ProcessChunkProgress(chunk)).ToArray()
                        );

                    // So it is possible for progress to occur in multiple chunks at once. For example,
                    // if we have a segment for test initialisation with some tests running at the same
                    // time in different segments.

                    // --- Completion stage ---

                    Dictionary<string, Segment[]> completeSegmentsByNamespace = chunkProcessorsByNamespace
                        .ToDictionary(
                            g => g.Key,
                            g => g.Value.SelectMany(processor => processor.ProcessCompletion(chunk)).ToArray()
                        );
                }

                // --- Supplementary processing stage ---
                // Process one at a time while we have time. Minimal time should be
                // given to intensive supplementary processing.
                // This is not invasive: it will add minor delay when summarising one segment
                // but will only be executed if we can't read anything from the buffer currently.
                if (DO_POST_PROCESSING && lastChunk != null && segmentsToProcess.Count > 0) {
                    Segment segment = segmentsToProcess.ElementAt(0);
                    segmentsToProcess.Remove(segment);

                    foreach (ChunkProcessor registeredProcessor in registeredProcessors) {
                        registeredProcessor.SummariseSegment(segment);
                    }
                }
            }

            // --- Double supplementary processing stage. ---
            if (DO_POST_PROCESSING) {
                while (segmentsToProcess.Count > 0) {
                    Segment segment = segmentsToProcess.ElementAt(0);
                    segmentsToProcess.Remove(segment);

                    foreach (ChunkProcessor registeredProcessor in registeredProcessors) {
                        registeredProcessor.SummariseSegment(segment);
                    }
                }
            }

            // --- Done stage ---
            // Everything should have either run or never been reached.
            // Will clean up incomplete stages.
            var allTouchedSegments = manager.Done().ToArray();

            foreach (Segment segment in allTouchedSegments) {
                Console.WriteLine($" --- {segment.Name} --- ");
                Console.Write(segment.AggregatedLogs());
            }
        }
    }

    struct Args {
        public bool Greedy { get; init; }
        public string[] ShellCommand { get; init; } = Array.Empty<string>();

        public Args(string[] args) {
            var splitPoint = args.Contains("--") ? Array.IndexOf(args, "--") + 1 : args.Length;

            var preExecSplitArgs = args[..splitPoint];

            if (splitPoint < args.Length) {
                var postExecSplitArgs = args[splitPoint..];

                this.ShellCommand = postExecSplitArgs;
            }

            if (preExecSplitArgs.Length >= 1) {
                this.Greedy = args[0].Equals("greedy", StringComparison.InvariantCultureIgnoreCase);
            }
        }
    }
}