using System.Threading.Channels;

namespace Pulsar.IO;

/// <summary>
/// Specifies how to read the input supplied to the standard input.
/// Only use greedy mode if you know that the logs will be supplied
/// individually; not batched. Otherwise, use NONE.
/// </summary>
[Flags]
public enum StdinListenerFlags {
    NONE = 2 >> 0,
    GREEDY = 2 >> 1,
}

public class StdinListener(ChannelWriter<Chunk> chunkChannel, StdinListenerFlags flags = StdinListenerFlags.GREEDY) {
    private const int MAX_CHUNK_SIZE = 4096;

    private bool listening;

    public void Start() {
        if (this.listening) {
            throw new Exception("Already listening");
        }

        this.listening = true;

        var listeningThread = new Thread(this.readStdin);
        listeningThread.Start();
    }

    public void Stop() {
        if (!this.listening) {
            throw new Exception("Not listening");
        }

        this.listening = false;
    }

    private void readStdin() {
        while (this.listening) {
            // STDIN doesn't have an end.
            // Let's chunk this: a Chunk can have a max Chunk size of MAX_CHUNK_SIZE.
            // Since we're always listening, we can "read to the end" by simply
            // taking what's available in the buffer at the time of reading,
            // and accepting that that will have a maximum size of the above.
            // ChunkProcessors should be resilient to this; they will always 
            // have to deal with fractured logging anyway, but the point is
            // to raise events whenever we *can* read from stdin.
            char[] buf = new char[4096];
            string? chunk;

            // This will spin whenever stdin has no input. LFG.
            if (flags.HasFlag(StdinListenerFlags.GREEDY)) {
                Console.In.Read(buf, 0, MAX_CHUNK_SIZE);
                chunk = new string(buf, 0, buf.Length);
            }
            else {
                chunk = Console.In.ReadLine();
            }

            if (!String.IsNullOrEmpty(chunk)) {
                var pubChunk = new Chunk(chunk, DateTime.Now);
                
                // Channels actually simplify our event consumption greatly.
                // Since production and consumption is 1:1, Channels will 
                // allows us to asynchronously write and read without needing
                // to explicitly spin waiting for events (since event consumption
                // can and probably will be slow).
                if (!chunkChannel.TryWrite(pubChunk)) {
                    throw new Exception("Channel was closed.");
                }
            }

            // We will never actually close the channel here.
        }
    }
}