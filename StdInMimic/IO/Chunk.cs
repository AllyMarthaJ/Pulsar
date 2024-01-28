namespace StdInMimic.IO; 

/// <summary>
/// Refers to a chunk of the standard input stream contributing to a series
/// of logs.
///
/// Assumes that: the stream is single threaded (e.g. one input stream
/// at a time) and that each chunk is consistent - either it's per-line
/// or per-log.
///
/// Chunks should be normalised prior to insertion into any data storage layer.
/// If they're not then :zany_face: oops
/// </summary>
/// <param name="Content">Content from the stream</param>
/// <param name="Timestamp">Time of insertion</param>
public record Chunk(string Content, DateTime Timestamp);