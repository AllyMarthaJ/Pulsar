using System.Threading.Channels;
using Pulsar.Backend.Data;
using Pulsar.Backend.Data.Managers;
using Pulsar.IO;

namespace Pulsar {
    internal class Program {
        // TODO: Array of SegmentManager
        private static SegmentManager manager = new SummarySegmentManager();
        private static ChunkProcessor[] registeredProcessors = Array.Empty<ChunkProcessor>();

        public static async Task Main(string[] args) {
            // STDIN is a long-running unbounded stream so I guess it makes
            // sense to replicate that here. We could/should probably 
            // bound this so we don't accidentally yeet ourselves if we don't
            // consume enough fast enough??
            // What should happen if we drop messages anyway...
            Channel<Chunk> chunkChannel = Channel.CreateUnbounded<Chunk>();
            var chunkReader = chunkChannel.Reader;

            StdinListener listener = new(chunkChannel);

            AppDomain.CurrentDomain.ProcessExit += (o, e) => {
                Console.WriteLine("Stopping listener...");
                listener.Stop();
                chunkChannel.Writer.Complete();
            };

            var chunkProcessorsByNamespace = registeredProcessors
                .GroupBy((ac) => ac.Namespace)
                .Where(g => !String.IsNullOrEmpty(g.Key))
                .ToDictionary((g) => g.Key!, g => g.ToArray());
            
            listener.Start();

            while (await chunkReader.WaitToReadAsync()) {
                while (chunkReader.TryRead(out Chunk? chunk)) {
                    // On chunk read, each registered log processor should,
                    // given an active manager, call various methods.
                    
                    // --- Activation stage ---
                    
                    Dictionary<string, IEnumerable<Segment>> activatedSegmentsByNamespace =
                        chunkProcessorsByNamespace
                            .ToDictionary(
                                g => g.Key,
                                g => g.Value.SelectMany(processor => processor.ProcessActivation(chunk))
                            );
                    
                    // The SegmentManager should be responsible for managing the deactivation
                    // of conflicting segments in each namespace. However, multiple activations 
                    // are not allowed to occur in the same namespace simultaneously.
                    
                    bool hasConflictingActivations = activatedSegmentsByNamespace
                        .Any((kv) => kv.Value.Count() > 1);
                    if (hasConflictingActivations) {
                        throw new Exception("Multiple conflicting segments activated");
                    }
                    
                    // --- Progress stage ---
                    
                    Dictionary<string, IEnumerable<Segment>> progressSegmentsByNamespace = chunkProcessorsByNamespace
                        .ToDictionary(
                            g => g.Key,
                            g => g.Value.SelectMany(processor => processor.ProcessChunkProgress(chunk))
                        );
                    
                    // So it is possible for progress to occur in multiple chunks at once. For example,
                    // if we have a segment for test initialisation with some tests running at the same
                    // time in different segments.
                    
                    // --- Completion stage ---
                    
                    Dictionary<string, IEnumerable<Segment>> completeSegmentsByNamespace = chunkProcessorsByNamespace
                        .ToDictionary(
                            g => g.Key,
                            g => g.Value.SelectMany(processor => processor.ProcessCompletion(chunk))
                        );
                }
            }
        }
    }
}