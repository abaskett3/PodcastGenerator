using System.Diagnostics;
using PodcastGenerator.Application.Scripts;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>Regression tests for script lines at the edge of the format rules, run through the whole pipeline and read off the
/// request body. Expected values come from the acceptance criteria named on each test.</summary>
public sealed class ScriptEdgeCaseTests : IDisposable
{
    private readonly Pipeline _pipeline = new();

    public void Dispose() => _pipeline.Dispose();

    private async Task<string> TranscriptOfAsync(string script)
    {
        var exitCode = await _pipeline.RunAsync(_pipeline.WriteScript(script));
        Assert.Equal(0, exitCode);
        return Assert.Single(_pipeline.Http.Requests).Transcript;
    }

    // AC-34: a line made only of cues is removed, however many cues are on it.
    [Fact]
    public async Task A_line_of_two_cues_is_removed()
    {
        var transcript = await TranscriptOfAsync("CECIL: One.\n\n[SFX: DOOR SLAM] [MUSIC: STING]\n\nCECIL: Two.\n");

        Assert.Equal("One.\n\nTwo.", transcript);
    }

    // AC-36: no word of the script is removed beyond AC-28 to AC-35, and a script that departs from the guide still runs.
    // AC-34 removes "a line consisting only of a square-bracket cue". This line has spoken words between two cues, so it is not
    // only a cue, and its words must still be narrated.
    [Fact]
    public async Task Spoken_words_between_two_cues_on_one_line_are_not_removed()
    {
        var transcript = await TranscriptOfAsync("CECIL: Before.\n\n[SFX: DOOR] and then he spoke [SFX: DOOR]\n\nCECIL: After.\n");

        Assert.Contains("and then he spoke", transcript, StringComparison.Ordinal);
    }

    // AC-34, D-10: a bracket inside a spoken line is narrated as written.
    [Fact]
    public async Task A_bracket_inside_a_spoken_line_is_narrated_as_written()
    {
        var transcript = await TranscriptOfAsync("CECIL: Hello [SFX: door] there.\n");

        Assert.Equal("Hello [SFX: door] there.", transcript);
    }

    // AC-33: a pause with a decimal duration is one tag and the decimal point does not end a sentence.
    [Fact]
    public async Task A_decimal_pause_is_one_tag_before_the_next_words()
    {
        var transcript = await TranscriptOfAsync("CECIL: Short.\n\n(PAUSE - 2.5 SECONDS)\n\nCECIL: Long.\n");

        var paragraphs = transcript.Split("\n\n");
        Assert.Equal("Short.", paragraphs[0]);
        Assert.Matches(@"^\[[^\[\]]*2\.5[^\[\]]*\] Long\.$", paragraphs[1]);
    }

    // AC-30: several speakers are all narrated, with the names stripped, in one voice.
    [Fact]
    public async Task Every_speaker_is_narrated_with_the_name_stripped_and_one_voice()
    {
        var transcript = await TranscriptOfAsync("CECIL: Good evening.\n\nCARLOS: Good evening to you.\n\nDANA: (WHISPERED) Shh.\n");

        var paragraphs = transcript.Split("\n\n");
        Assert.Equal(3, paragraphs.Length);
        Assert.Equal("Good evening.", paragraphs[0]);
        Assert.Equal("Good evening to you.", paragraphs[1]);
        Assert.Matches(@"^\[[^\[\]]+\] Shh\.$", paragraphs[2]);
        Assert.Equal("Umbriel", Assert.Single(_pipeline.Http.Requests).Json.GetProperty("voice").GetString());
    }

    // AC-34: every cue form shown in docs/script-writing-guide.md is removed, alone on its line.
    [Theory]
    [InlineData("[MUSIC: THEME – DESCRIPTION OF THE SOUND (FADE UP, THEN UNDER)]")]
    [InlineData("[SFX: STATIC - FADE OUT]")]
    [InlineData("[MUSIC: LOW, OMINOUS (UNDER)]")]
    [InlineData("[FADE OUT]")]
    [InlineData("[SFX: WHAT THE LISTENER HEARS]")]
    [InlineData("[SFX: PHONE [RINGS] TWICE]")]
    public async Task Every_cue_form_of_the_script_guide_is_removed(string cue)
    {
        var transcript = await TranscriptOfAsync($"CECIL: One.\n\n{cue}\n\nCECIL: Two.\n");

        Assert.Equal("One.\n\nTwo.", transcript);
    }

    // AC-36: words before, after or between cues are narrated as written, and nothing else on the line is lost.
    [Theory]
    [InlineData("[SFX: DOOR] Hello there.", "[SFX: DOOR] Hello there.")]
    [InlineData("Hello there. [SFX: DOOR]", "Hello there. [SFX: DOOR]")]
    [InlineData("[SFX: DOOR] [MUSIC: STING] and words", "[SFX: DOOR] [MUSIC: STING] and words")]
    [InlineData("[SFX: DOOR] words [MUSIC: STING] more words [FADE OUT]", "[SFX: DOOR] words [MUSIC: STING] more words [FADE OUT]")]
    public async Task A_line_with_words_and_cues_is_narrated_as_written(string line, string expected)
    {
        var transcript = await TranscriptOfAsync($"CECIL: One.\n\n{line}\n");

        Assert.Equal($"One.\n\n{expected}", transcript);
    }

    // The cue pattern must not take a long time on a long line that is not a cue (it is applied to every line of a script).
    [Fact]
    public void The_cue_check_is_fast_on_very_long_lines_that_are_not_cues()
    {
        var parser = new ScriptParser();
        var inputs = new[]
        {
            "[" + new string('a', 200_000),
            string.Concat(Enumerable.Repeat("[a] ", 50_000)) + "words",
            "[" + string.Concat(Enumerable.Repeat("[a", 50_000)),
            string.Concat(Enumerable.Repeat("[", 50_000)),
        };

        foreach (var input in inputs)
        {
            var clock = Stopwatch.StartNew();
            var script = parser.Parse("CECIL: One.\n\n" + input + "\n");
            clock.Stop();

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Parsing a {input.Length} character line took {clock.Elapsed}.");
            Assert.True(script.HasSpeech);
        }
    }
}
