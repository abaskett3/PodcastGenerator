using PodcastGenerator.Application.Narration;
using PodcastGenerator.Application.Scripts;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Narration;

public class ChunkPlannerTests
{
    private readonly ChunkPlanner _planner = new();

    private static RenderedSegment Segment(params string[] paragraphs) => new(paragraphs);

    [Fact]
    public void A_small_script_is_one_chunk()
    {
        var plan = _planner.Plan([Segment("One.", "Two."), Segment("Three.")], 1000);

        var chunk = Assert.Single(plan.Chunks);
        Assert.Equal(1, chunk.Number);
        Assert.Equal("One.\n\nTwo.\n\nThree.", chunk.Transcript);
        Assert.Empty(plan.Warnings);
    }

    // D-11: consecutive segments are packed into one chunk up to the chunk size.
    [Fact]
    public void Consecutive_segments_are_packed_up_to_the_chunk_size()
    {
        var a = new string('a', 40);
        var b = new string('b', 40);
        var c = new string('c', 40);

        var plan = _planner.Plan([Segment(a), Segment(b), Segment(c)], 90);

        Assert.Equal([$"{a}\n\n{b}", c], plan.Chunks.Select(chunk => chunk.Transcript));
        Assert.Equal([1, 2], plan.Chunks.Select(chunk => chunk.Number));
    }

    // AC-48: split at segment boundaries first.
    [Fact]
    public void A_chunk_boundary_falls_on_a_segment_boundary_when_segments_fit()
    {
        var plan = _planner.Plan(
            [Segment(new string('a', 30), new string('b', 30)), Segment(new string('c', 30), new string('d', 30))],
            70);

        Assert.Equal(2, plan.Chunks.Count);
        Assert.Equal($"{new string('a', 30)}\n\n{new string('b', 30)}", plan.Chunks[0].Transcript);
        Assert.Equal($"{new string('c', 30)}\n\n{new string('d', 30)}", plan.Chunks[1].Transcript);
    }

    // AC-48: then at paragraph boundaries.
    [Fact]
    public void A_segment_larger_than_a_chunk_is_split_at_paragraph_boundaries()
    {
        var paragraphs = Enumerable.Range(0, 6).Select(index => new string((char)('a' + index), 30)).ToArray();

        var plan = _planner.Plan([Segment(paragraphs)], 70);

        Assert.Equal(3, plan.Chunks.Count);
        Assert.Equal($"{paragraphs[0]}\n\n{paragraphs[1]}", plan.Chunks[0].Transcript);
        Assert.Equal($"{paragraphs[4]}\n\n{paragraphs[5]}", plan.Chunks[2].Transcript);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void The_tail_of_a_split_segment_is_packed_with_the_next_segment()
    {
        var a = new string('a', 50);
        var b = new string('b', 50);
        var c = new string('c', 10);
        var d = new string('d', 5);

        var plan = _planner.Plan([Segment(a, b, c), Segment(d)], 70);

        Assert.Equal([a, $"{b}\n\n{c}\n\n{d}"], plan.Chunks.Select(chunk => chunk.Transcript));
    }

    // AC-48: an oversized paragraph is split at a sentence end, with a warning.
    [Fact]
    public void An_oversized_paragraph_is_split_at_sentence_ends_with_a_warning()
    {
        var paragraph = "First sentence here. Second sentence here! Third sentence here? Fourth sentence here.";

        var plan = _planner.Plan([Segment(paragraph)], 45);

        Assert.All(plan.Chunks, chunk => Assert.True(chunk.Transcript.Length <= 45, chunk.Transcript));
        Assert.All(plan.Chunks, chunk => Assert.Matches(@"[.!?]$", chunk.Transcript));
        Assert.Equal(paragraph, string.Join(' ', plan.Chunks.Select(chunk => chunk.Transcript)));
        var warning = Assert.Single(plan.Warnings);
        Assert.Contains("Segment 1, paragraph 1", warning);
        Assert.Contains("split at sentence ends", warning);
    }

    [Fact]
    public void A_single_sentence_longer_than_a_chunk_is_kept_whole_and_the_warning_says_so()
    {
        var sentence = "This one sentence has no end until the very last word of it";

        var plan = _planner.Plan([Segment(sentence + ".")], 20);

        Assert.Equal(sentence + ".", Assert.Single(plan.Chunks).Transcript);
        Assert.Contains("One sentence is longer than the chunk size", Assert.Single(plan.Warnings));
    }

    // AC-48: never between a tag and its text.
    [Fact]
    public void A_tag_is_never_separated_from_its_text_when_a_paragraph_is_split()
    {
        var paragraph = "[whispers] One two three. [emphasis] Four five six. Seven eight nine. [short pause] Ten eleven twelve.";

        var plan = _planner.Plan([Segment(paragraph)], 40);

        foreach (var chunk in plan.Chunks)
        {
            Assert.DoesNotMatch(@"\[[^\]]*\]\s*$", chunk.Transcript); // a chunk never ends with a bare tag
        }

        Assert.Equal(paragraph, string.Join(' ', plan.Chunks.Select(chunk => chunk.Transcript)));
    }

    [Fact]
    public void A_full_stop_inside_a_tag_is_not_a_sentence_end()
    {
        var sentences = SplitViaPlanner("[a. b] Text here. More text.");

        Assert.Equal(["[a. b] Text here.", "More text."], sentences);
    }

    private static IReadOnlyList<string> SplitViaPlanner(string text)
    {
        // SentenceSplitter is internal; its behavior is observable through the planner with a limit that forces a split.
        var plan = new ChunkPlanner().Plan([new RenderedSegment([text])], 20);
        return plan.Chunks.Select(chunk => chunk.Transcript).ToList();
    }

    // AC-52: no text is missing, duplicated or reordered.
    [Fact]
    public void Joining_the_chunks_gives_back_every_paragraph_once_and_in_order()
    {
        var segments = Enumerable.Range(1, 12)
            .Select(s => Segment(Enumerable.Range(1, 5).Select(p => $"Segment {s} paragraph {p} has some words in it.").ToArray()))
            .ToList();

        var plan = _planner.Plan(segments, 300);

        var expected = segments.SelectMany(segment => segment.Paragraphs).ToList();
        var actual = plan.Chunks.SelectMany(chunk => chunk.Transcript.Split("\n\n")).ToList();
        Assert.Equal(expected, actual);
        Assert.Equal(Enumerable.Range(1, plan.Chunks.Count), plan.Chunks.Select(chunk => chunk.Number));
        Assert.All(plan.Chunks, chunk => Assert.True(chunk.Transcript.Length <= 300));
    }

    [Fact]
    public void The_sample_script_is_planned_within_the_configured_chunk_size()
    {
        var text = File.ReadAllText(
            RepositoryFiles.Combine("docs", "sample-scripts", "QE-3395-RC-09-17-26-nightvale-podcast-script.txt"));
        var segments = new TranscriptRenderer(new TagVocabulary()).Render(new ScriptParser().Parse(text));

        var plan = _planner.Plan(segments, NarrationSettings.MaxChunkCharacters);

        Assert.True(plan.Chunks.Count > 1);
        Assert.All(plan.Chunks, chunk => Assert.True(chunk.Transcript.Length <= NarrationSettings.MaxChunkCharacters));
        Assert.Empty(plan.Warnings);
        var expected = segments.SelectMany(segment => segment.Paragraphs).ToList();
        Assert.Equal(expected, plan.Chunks.SelectMany(chunk => chunk.Transcript.Split("\n\n")).ToList());
    }

    [Fact]
    public void Nothing_to_plan_gives_no_chunks()
    {
        Assert.Empty(_planner.Plan([], 100).Chunks);
    }

    [Fact]
    public void A_chunk_size_below_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _planner.Plan([Segment("x")], 0));
    }
}
