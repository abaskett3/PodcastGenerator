namespace PodcastGenerator.Domain.Narration;

/// <summary>One part of the script that is narrated with one request. <see cref="Transcript"/> is the text the model
/// speaks, already containing the inline tags.</summary>
public sealed record NarrationChunk(int Number, string Transcript);
