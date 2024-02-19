﻿using System.Text.RegularExpressions;
using System.Threading.Channels;
using Pulsar.Backend.Data;
using Pulsar.Backend.Data.ChunkProcessors;
using Pulsar.Backend.Data.Managers;
using StdInMimic.IO;

namespace Pulsar {
    internal class Program {
        private const int MAX_LEN = 230;
        private const bool DO_POST_PROCESSING = false;

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
            }, !DO_POST_PROCESSING)
        };

        private static Dictionary<string, ChunkProcessor[]> chunkProcessorsByNamespace = registeredProcessors
            .GroupBy((ac) => ac.Namespace)
            .ToDictionary((g) => String.IsNullOrEmpty(g.Key) ? "--reserved--" : g.Key, g => g.ToArray());

        public static async Task Main(string[] args) {
            Stack<Segment> segmentsToProcess = new();
            
            manager.SegmentModified += (sender, ev) => {
                if (DO_POST_PROCESSING) {
                    segmentsToProcess.Push(ev.Segment);
                }
                
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
            
            StdinListener listener = new(chunkChannel,
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
                if (DO_POST_PROCESSING) {
                    if (lastChunk != null && segmentsToProcess.TryPop(out Segment? segment)) {
                        foreach (ChunkProcessor registeredProcessor in registeredProcessors) {
                            registeredProcessor.SummariseSegment(segment);
                        }
                    }   
                }
            }
            
            // --- Double supplementary processing stage. ---
            if (DO_POST_PROCESSING) {
                while (segmentsToProcess.TryPop(out Segment? segment)) {
                    foreach (ChunkProcessor registeredProcessor in registeredProcessors) {
                        registeredProcessor.SummariseSegment(segment);
                    }
                }   
            }
        }
    }
}