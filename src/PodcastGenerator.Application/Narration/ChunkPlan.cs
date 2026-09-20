using PodcastGenerator.Domain.Narration;

namespace PodcastGenerator.Application.Narration;

public sealed record ChunkPlan(IReadOnlyList<NarrationChunk> Chunks, IReadOnlyList<string> Warnings);
