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
}
