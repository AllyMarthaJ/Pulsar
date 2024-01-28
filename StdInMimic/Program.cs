using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using StdInMimic.IO;

namespace StdInMimic;

enum ProgramMode {
    RECORD,
    MIMIC
}

internal class Program {
    private static Channel<Chunk>? stdinListenerChannel;
    private static StdinListener? stdinListener;

    private static List<MimicLog> mimicStdInLogs = new();

    private static string? filePath;

    public static async Task Main(string?[] args) {
        if (args.Length < 1) {
            Console.WriteLine("Usage: StdInMimic [record|mimic] [filename] [shouldSleep(t/f)]");
            return;
        }

        ProgramMode mode;
        bool shouldSleep = true;
        if (args[0] == "record") {
            if (args.Length >= 2) {
                filePath = args[1];
            }
            else {
                filePath = Guid.NewGuid() + ".json";
            }

            mode = ProgramMode.RECORD;
        }
        else if (args[0] == "mimic") {
            if (args.Length < 2) {
                Console.WriteLine("Usage: StdInMimic mimic [filename] [shouldSleep(t/f)]");
                return;
            }

            filePath = args[1];

            if (args.Length >= 3) {
                shouldSleep = args[2] == "t" || args[2] == "T";
            }

            mode = ProgramMode.MIMIC;
        }
        else {
            Console.WriteLine("Usage: StdInMimic [record|play] [filename] [shouldSleep(t/f)]");
            return;
        }

        var fi = new FileInfo(filePath);

        switch (mode) {
            case ProgramMode.RECORD:
                Console.WriteLine("Now writing stdin to " + fi.FullName);
                Console.WriteLine("Terminate at any time...");
                
                // init listening channel
                stdinListenerChannel = Channel.CreateUnbounded<Chunk>();
                stdinListener = new(stdinListenerChannel.Writer);
                stdinListener.Start();

                var chunkReader = stdinListenerChannel.Reader;

                DateTime? baseTs = null;
                
                while (await chunkReader.WaitToReadAsync()) {
                    while (chunkReader.TryRead(out Chunk? chunk)) {
                        TimeSpan os = baseTs == null ? new TimeSpan(0) : (chunk.Timestamp - baseTs).Value;
                        
                        mimicStdInLogs.Add(new MimicLog(chunk.Content, os.Milliseconds));

                        baseTs = chunk.Timestamp;

                        // The beauty of this is that it can be slow, but we don't care:
                        // channels will allow us to consume this regardless.
                        string serialised = JsonSerializer.Serialize(mimicStdInLogs);
                        await File.WriteAllTextAsync(filePath, serialised);
                        
                        Console.WriteLine($"[{chunk.Timestamp.ToLongTimeString()}] Writing log....");
                    }
                }

                break;
            case ProgramMode.MIMIC:
                var fContent = await File.ReadAllTextAsync(filePath);
                mimicStdInLogs = JsonSerializer.Deserialize<List<MimicLog>>(fContent)!;
                
                foreach (var mimicStdInLog in mimicStdInLogs) {
                    await Console.Out.WriteAsync(mimicStdInLog.Log);

                    if (shouldSleep) {
                        if (mimicStdInLog.TimeSinceLastEvent >= Int32.MaxValue) {
                            // wtf yo
                            throw new Exception("Something went wrong and y'all high");
                        }
                        else {
                            Thread.Sleep(Convert.ToInt32(mimicStdInLog.TimeSinceLastEvent));
                        }
                    }
                }
                
                break;
        }
    }
}