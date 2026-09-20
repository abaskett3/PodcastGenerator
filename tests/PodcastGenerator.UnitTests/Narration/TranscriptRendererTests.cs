using PodcastGenerator.Application.Narration;
using PodcastGenerator.Application.Scripts;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Narration;

public class TranscriptRendererTests
{
    private readonly ScriptParser _parser = new();
    private readonly TranscriptRenderer _renderer = new(new TagVocabulary());

    private List<string> Paragraphs(string script) =>
        _renderer.Render(_parser.Parse(script)).SelectMany(segment => segment.Paragraphs).ToList();

    // AC-32
    [Fact]
    public void A_direction_becomes_a_tag_placed_before_the_lines_text()
    {
        Assert.Equal(["[whispers] The sky is turning purple."], Paragraphs("CECIL: (WHISPERED) The sky is turning purple."));
    }

    [Fact]
    public void An_unlisted_direction_becomes_its_own_lower_cased_words_in_one_tag()
    {
        Assert.Equal(["[weary, slow] Fine."], Paragraphs("CECIL: (WEARY, SLOW) Fine."));
    }

    // AC-33: the pause is passed as a tag, and its words are not spoken.
    [Fact]
    public void A_beat_becomes_a_pause_tag_that_stays_with_the_text_that_follows()
    {
        var paragraphs = Paragraphs("CECIL: One.\n\n(BEAT)\n\nCECIL: Two.");

        Assert.Equal(["One.", "[short pause] Two."], paragraphs);
        Assert.DoesNotContain(paragraphs, paragraph => paragraph.Contains("BEAT", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("(PAUSE - 3 SECONDS)", "[pause for 3 seconds] Two.")]
    [InlineData("(PAUSE - 1 SECOND)", "[pause for 1 second] Two.")]
    [InlineData("(PAUSE - 2.5 SECONDS)", "[pause for 2.5 seconds] Two.")]
    public void A_timed_pause_carries_its_duration_into_the_tag(string pause, string expected)
    {
        Assert.Equal(["One.", expected], Paragraphs($"CECIL: One.\n\n{pause}\n\nCECIL: Two."));
    }

    [Fact]
    public void A_pause_with_nothing_after_it_is_dropped()
    {
        Assert.Equal(["One."], Paragraphs("CECIL: One.\n\n(BEAT)\n\n[FADE OUT]\n\nEND\n"));
    }

    [Fact]
    public void A_pause_at_a_segment_end_goes_with_the_first_paragraph_of_the_next_segment()
    {
        var segments = _renderer.Render(_parser.Parse("### A\n\nCECIL: One.\n\n(BEAT)\n\n### B\n\nCECIL: Two."));

        Assert.Equal(["One."], segments[0].Paragraphs);
        Assert.Equal(["[short pause] Two."], segments[1].Paragraphs);
    }

    // AC-35, D-7
    [Fact]
    public void Emphasis_places_a_tag_immediately_before_the_emphasized_words()
    {
        Assert.Equal(["No, I am speaking of [emphasis] Instance logs."], Paragraphs("CECIL: No, I am speaking of *Instance logs*."));
    }

    // AC-37, D-9
    [Fact]
    public void Adjacent_tags_are_merged_into_one_bracket_separated_by_a_comma()
    {
        Assert.Equal(["[whispers, emphasis] Instance logs."], Paragraphs("CECIL: (WHISPERED) *Instance logs*."));
        Assert.Equal(["[short pause, emphasis] Instance logs."], Paragraphs("(BEAT)\n\nCECIL: *Instance logs*."));
        Assert.Equal(["[pause for 3 seconds, whispers, emphasis] Hush."], Paragraphs("(PAUSE - 3 SECONDS)\n\nCECIL: (WHISPERED) *Hush*."));
    }

    [Theory]
    [InlineData("CECIL: (WHISPERED) *One* and *two*. (BEAT)\n\n(BEAT)\n\nCECIL: *Three*.")]
    [InlineData("(BEAT)\n\n(PAUSE - 3 SECONDS)\n\nCECIL: (WHISPERED) *A* *B* *C*.")]
    public void No_two_tags_are_ever_adjacent_in_the_text_sent(string script)
    {
        foreach (var paragraph in Paragraphs(script))
        {
            Assert.DoesNotMatch(@"\]\s*\[", paragraph);
        }
    }

    [Fact]
    public void Two_emphasized_phrases_each_get_their_own_tag()
    {
        Assert.Equal(["Not [emphasis] one. It is [emphasis] six."], Paragraphs("CECIL: Not *one*. It is *six*."));
    }

    // AC-36
    [Fact]
    public void The_same_script_gives_the_same_transcript_every_time()
    {
        const string script = "CECIL: (WHISPERED) One *two* three.\n\n(BEAT)\n\nCECIL: Four.";

        Assert.Equal(Paragraphs(script), Paragraphs(script));
    }

    [Fact]
    public void Text_outside_tags_is_never_changed()
    {
        var paragraphs = Paragraphs("CECIL: It's 10 o'clock — (really), and [nothing] else.");

        Assert.Equal(["It's 10 o'clock — (really), and [nothing] else."], paragraphs);
    }

    // AC-38: golden test on the sample.
    [Fact]
    public void The_sample_script_renders_the_tags_next_to_the_right_text()
    {
        var text = File.ReadAllText(
            RepositoryFiles.Combine("docs", "sample-scripts", "QE-3395-RC-09-17-26-nightvale-podcast-script.txt"));

        var transcript = string.Join("\n\n", _renderer.Render(_parser.Parse(text)).SelectMany(segment => segment.Paragraphs));

        Assert.Contains("[whispers] The sky is turning that shade of purple again.", transcript);
        Assert.Contains("[emphasis] Instance logs.", transcript);
        Assert.Contains("Good evening, Night Vale. I'm Cecil Palmer.", transcript);
        Assert.DoesNotContain("PROGRAMME", transcript);
        Assert.DoesNotContain("SEGMENT", transcript);
        Assert.DoesNotContain("CECIL:", transcript);
        Assert.DoesNotContain("CONT'D", transcript);
        Assert.DoesNotContain("[SFX", transcript);
        Assert.DoesNotContain("[MUSIC", transcript);
        Assert.DoesNotContain("FADE OUT", transcript);
        Assert.DoesNotContain('*', transcript);
        Assert.DoesNotContain("\nEND\n", $"\n{transcript}\n");
        Assert.DoesNotMatch(@"\]\s*\[", transcript);
    }
}
