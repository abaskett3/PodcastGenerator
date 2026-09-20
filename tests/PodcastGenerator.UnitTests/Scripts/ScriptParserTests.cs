using PodcastGenerator.Application.Scripts;
using PodcastGenerator.Domain.Scripts;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Scripts;

public class ScriptParserTests
{
    private readonly ScriptParser _parser = new();

    private static IReadOnlyList<ScriptBlock> AllBlocks(Script script) => script.Segments.SelectMany(segment => segment.Blocks).ToList();

    private static List<SpeechBlock> Speech(Script script) => AllBlocks(script).OfType<SpeechBlock>().ToList();

    // AC-28
    [Fact]
    public void Headings_are_not_spoken()
    {
        var script = _parser.Parse("# TITLE\n\n## SCENE 1 - INT. STUDIO\n\n### SEGMENT 1 - OPENING\n\nCECIL: Hello there.\n");

        var block = Assert.Single(Speech(script));
        Assert.Equal("Hello there.", block.PlainText);
    }

    [Fact]
    public void A_hash_that_is_not_followed_by_a_space_is_spoken_text()
    {
        var script = _parser.Parse("CECIL: Now for the news.\n\n#1 on the charts tonight.\n");

        Assert.Equal(["Now for the news.", "#1 on the charts tonight."], Speech(script).Select(block => block.PlainText));
    }

    // AC-29
    [Fact]
    public void The_four_named_header_labels_and_END_are_not_spoken()
    {
        var script = _parser.Parse(
            "# T\n\nPROGRAMME: Show\n\nEPISODE: 1\n\nSTYLE: dry\n\nCAST: CECIL (host)\n\n## SCENE 1\n\n### SEGMENT 1\n\nCECIL: Spoken.\n\nEND\n");

        Assert.Equal(["Spoken."], Speech(script).Select(block => block.PlainText));
    }

    [Fact]
    public void Named_header_labels_are_dropped_wherever_they_appear()
    {
        var script = _parser.Parse("### SEGMENT 1\n\nEPISODE: appears late\n\nCECIL: Spoken.\n");

        Assert.Equal(["Spoken."], Speech(script).Select(block => block.PlainText));
    }

    // D-4
    [Fact]
    public void Other_label_lines_before_the_first_scene_heading_are_header_lines()
    {
        var script = _parser.Parse("# T\n\nDURATION: 10 minutes\n\nSTUDIO: Studio 4\n\n## SCENE 1\n\n### SEGMENT 1\n\nCECIL: Spoken.\n");

        Assert.Equal(["Spoken."], Speech(script).Select(block => block.PlainText));
    }

    [Fact]
    public void A_script_with_no_headings_is_not_swallowed_by_the_header_rule()
    {
        // AC-36: a script that departs from the guide still runs. Without a scene heading there is no header block.
        var script = _parser.Parse("CECIL: Good evening.\n\nMARGO: And good night.\n");

        Assert.Equal(["Good evening.", "And good night."], Speech(script).Select(block => block.PlainText));
    }

    // AC-30, D-5
    [Fact]
    public void Speaker_names_are_stripped_and_unlabelled_paragraphs_are_kept()
    {
        var script = _parser.Parse("CECIL: First paragraph.\n\nSecond paragraph of the same speech.\n\nMARY-ANN O'BRIEN: A second voice.\n");

        Assert.Equal(
            ["First paragraph.", "Second paragraph of the same speech.", "A second voice."],
            Speech(script).Select(block => block.PlainText));
    }

    [Fact]
    public void Unlabelled_text_before_any_speaker_is_narrated_as_written()
    {
        var script = _parser.Parse("Welcome, everyone.\n");

        Assert.Equal("Welcome, everyone.", Assert.Single(Speech(script)).PlainText);
    }

    [Fact]
    public void A_colon_later_in_a_line_does_not_make_a_speaker()
    {
        var script = _parser.Parse("CECIL: Note the time: nine o'clock.\n\nThe reason: none given.\n");

        Assert.Equal(["Note the time: nine o'clock.", "The reason: none given."], Speech(script).Select(block => block.PlainText));
    }

    // AC-31, D-3
    [Theory]
    [InlineData("CECIL: (CONT'D) We have corrected this.")]
    [InlineData("CECIL: (CONT’D) We have corrected this.")]
    [InlineData("CECIL: (cont'd) We have corrected this.")]
    public void CONTD_is_not_spoken_and_produces_no_direction(string line)
    {
        var block = Assert.Single(Speech(_parser.Parse(line)));

        Assert.Equal("We have corrected this.", block.PlainText);
        Assert.Null(block.Direction);
    }

    // AC-32, D-6
    [Fact]
    public void A_delivery_direction_after_the_speaker_becomes_the_block_direction()
    {
        var block = Assert.Single(Speech(_parser.Parse("CECIL: (WHISPERED) The sky is turning purple.")));

        Assert.Equal("WHISPERED", block.Direction);
        Assert.Equal("The sky is turning purple.", block.PlainText);
    }

    [Fact]
    public void A_direction_after_CONTD_is_still_a_direction()
    {
        var block = Assert.Single(Speech(_parser.Parse("CECIL: (CONT'D) (WHISPERED, SLOW) Quiet now.")));

        Assert.Equal("WHISPERED, SLOW", block.Direction);
        Assert.Equal("Quiet now.", block.PlainText);
    }

    [Fact]
    public void The_direction_applies_to_its_own_paragraph_only()
    {
        var blocks = Speech(_parser.Parse("CECIL: (WHISPERED) Quiet.\n\nLouder paragraph.\n"));

        Assert.Equal("WHISPERED", blocks[0].Direction);
        Assert.Null(blocks[1].Direction);
    }

    [Fact]
    public void Parentheses_inside_spoken_text_are_narrated_as_written()
    {
        // D-8: only a direction directly after the speaker name is a direction.
        var block = Assert.Single(Speech(_parser.Parse("CECIL: The word Nguyen (win) is hard.")));

        Assert.Equal("The word Nguyen (win) is hard.", block.PlainText);
        Assert.Null(block.Direction);
    }

    // AC-33, D-8
    [Fact]
    public void A_beat_on_its_own_line_is_a_pause_without_a_duration()
    {
        var script = _parser.Parse("CECIL: One.\n\n(BEAT)\n\nCECIL: Two.\n");

        var pause = Assert.IsType<PauseBlock>(AllBlocks(script)[1]);
        Assert.Null(pause.Duration);
    }

    [Theory]
    [InlineData("(PAUSE - 3 SECONDS)", 3)]
    [InlineData("(PAUSE – 1 SECOND)", 1)]
    [InlineData("(pause - 2.5 seconds)", 2.5)]
    public void A_timed_pause_on_its_own_line_carries_its_duration(string line, double seconds)
    {
        var script = _parser.Parse($"CECIL: One.\n\n{line}\n\nCECIL: Two.\n");

        var pause = Assert.IsType<PauseBlock>(AllBlocks(script)[1]);
        Assert.Equal(TimeSpan.FromSeconds(seconds), pause.Duration);
    }

    [Fact]
    public void Other_parentheticals_on_their_own_line_are_narrated_as_written()
    {
        var script = _parser.Parse("(CLEARS THROAT)\n");

        Assert.Equal("(CLEARS THROAT)", Assert.Single(Speech(script)).PlainText);
    }

    // AC-34, D-10
    [Theory]
    [InlineData("[SFX: SOFT RADIO STATIC]")]
    [InlineData("[MUSIC: THEME - EERIE (FADE UP, THEN UNDER)]")]
    [InlineData("[FADE OUT]")]
    [InlineData("[SFX: DOOR SLAM] [MUSIC: STING]")]
    [InlineData("[SFX: DOOR SLAM][MUSIC: STING]")]
    [InlineData("[SFX: DOOR [SLAM]]")]
    public void A_whole_line_cue_is_removed(string cue)
    {
        var script = _parser.Parse($"CECIL: Before.\n\n{cue}\n\nCECIL: (CONT'D) After.\n");

        Assert.Equal(["Before.", "After."], Speech(script).Select(block => block.PlainText));
        Assert.Empty(AllBlocks(script).OfType<PauseBlock>());
    }

    // AC-34, AC-36: a line is removed only when it is nothing but cues. Words before, between or after cues are kept as written.
    [Theory]
    [InlineData("[SFX: DOOR] and then he spoke [SFX: DOOR]")]
    [InlineData("[SFX: DOOR] and then he spoke")]
    [InlineData("and then he spoke [SFX: DOOR]")]
    [InlineData("[SFX: DOOR] and then he spoke [SFX: DOOR] and more")]
    public void Spoken_words_beside_cues_on_one_line_are_kept_as_written(string line)
    {
        var block = Assert.Single(Speech(_parser.Parse(line)));

        Assert.Equal(line, block.PlainText);
    }

    [Fact]
    public void A_bracket_inside_a_spoken_line_is_narrated_as_written()
    {
        var block = Assert.Single(Speech(_parser.Parse("CECIL: The sign said [closed] all week.")));

        Assert.Equal("The sign said [closed] all week.", block.PlainText);
    }

    // AC-35, D-7
    [Fact]
    public void Emphasis_asterisks_are_removed_and_the_words_marked()
    {
        var block = Assert.Single(Speech(_parser.Parse("CECIL: No, I'm speaking of *Instance logs*. Truly.")));

        Assert.Equal("No, I'm speaking of Instance logs. Truly.", block.PlainText);
        Assert.Equal(
            [("No, I'm speaking of ", false), ("Instance logs", true), (". Truly.", false)],
            block.Spans.Select(span => (span.Text, span.IsEmphasized)));
        Assert.DoesNotContain('*', block.PlainText);
    }

    [Fact]
    public void An_unpaired_asterisk_is_left_as_written()
    {
        var block = Assert.Single(Speech(_parser.Parse("CECIL: A lone * star.")));

        Assert.Equal("A lone * star.", block.PlainText);
    }

    // Segments (AC-48 depends on them)
    [Fact]
    public void Segment_headings_start_new_segments_and_scene_headings_do_not()
    {
        var script = _parser.Parse(
            "## SCENE 1\n\n### SEGMENT 1\n\nCECIL: One.\n\n### SEGMENT 2\n\nCECIL: Two.\n\n## SCENE 2\n\nCECIL: Still segment two.\n");

        Assert.Equal(2, script.Segments.Count);
        Assert.Equal(["One."], script.Segments[0].Blocks.OfType<SpeechBlock>().Select(block => block.PlainText));
        Assert.Equal(["Two.", "Still segment two."], script.Segments[1].Blocks.OfType<SpeechBlock>().Select(block => block.PlainText));
    }

    [Fact]
    public void A_segment_with_nothing_to_narrate_is_not_kept()
    {
        var script = _parser.Parse("### SEGMENT 1\n\n[SFX: NOISE]\n\n### SEGMENT 2\n\nCECIL: Spoken.\n");

        Assert.Single(script.Segments);
    }

    // AC-20
    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    [InlineData("# TITLE\n\n[SFX: NOISE]\n\n(BEAT)\n\nEND\n")]
    public void A_script_with_no_narratable_text_has_no_speech(string text)
    {
        Assert.False(_parser.Parse(text).HasSpeech);
    }

    // D-16
    [Fact]
    public void A_byte_order_mark_and_windows_or_old_mac_line_endings_are_accepted()
    {
        var script = _parser.Parse("﻿# T\r\n\r\n## S\r\n\r\nCECIL: One.\r\n\r\nTwo.\rThree.");

        Assert.Equal(["One.", "Two.", "Three."], Speech(script).Select(block => block.PlainText));
    }

    [Fact]
    public void Non_ascii_text_is_kept_exactly()
    {
        var block = Assert.Single(Speech(_parser.Parse("CECIL: One car—just one—and café lights.")));

        Assert.Equal("One car—just one—and café lights.", block.PlainText);
    }

    // AC-36
    [Fact]
    public void Parsing_is_deterministic_and_never_changes_spoken_words()
    {
        const string text = "CECIL: (WHISPERED) The *quick* brown fox.\n\n(BEAT)\n\nSecond, with (parens) and [brackets].\n";

        var first = _parser.Parse(text);
        var second = _parser.Parse(text);

        Assert.Equal(
            Speech(first).Select(block => (block.Direction, block.PlainText)),
            Speech(second).Select(block => (block.Direction, block.PlainText)));
        Assert.Equal("The quick brown fox.", Speech(first)[0].PlainText);
        Assert.Equal("Second, with (parens) and [brackets].", Speech(first)[1].PlainText);
    }

    // AC-21
    [Fact]
    public void A_script_of_three_thousand_lines_is_parsed_completely()
    {
        var lines = new List<string> { "# BIG", "", "## SCENE 1", "" };
        for (var segment = 1; segment <= 100; segment++)
        {
            lines.Add($"### SEGMENT {segment}");
            lines.Add(string.Empty);
            for (var paragraph = 1; paragraph <= 14; paragraph++)
            {
                lines.Add($"CECIL: Paragraph {paragraph} of segment {segment}.");
                lines.Add(string.Empty);
            }

            lines.Add("[SFX: A CUE]");
            lines.Add(string.Empty);
        }

        Assert.True(lines.Count > 3000);
        var script = _parser.Parse(string.Join('\n', lines));

        Assert.Equal(100, script.Segments.Count);
        Assert.Equal(1400, Speech(script).Count);
    }

    // AC-38: golden test on the sample script.
    [Fact]
    public void The_sample_script_keeps_only_narrated_content()
    {
        var text = File.ReadAllText(
            RepositoryFiles.Combine("docs", "sample-scripts", "QE-3395-RC-09-17-26-nightvale-podcast-script.txt"));

        var script = _parser.Parse(text);

        Assert.Equal(10, script.Segments.Count);
        var spoken = string.Join('\n', Speech(script).Select(block => block.PlainText));
        Assert.DoesNotContain("PROGRAMME", spoken);
        Assert.DoesNotContain("EPISODE", spoken);
        Assert.DoesNotContain("CAST", spoken);
        Assert.DoesNotContain("SEGMENT", spoken);
        Assert.DoesNotContain("SCENE", spoken);
        Assert.DoesNotContain("CECIL:", spoken);
        Assert.DoesNotContain("CONT'D", spoken);
        Assert.DoesNotContain("[SFX", spoken);
        Assert.DoesNotContain("[MUSIC", spoken);
        Assert.DoesNotContain("FADE OUT", spoken);
        Assert.DoesNotContain('*', spoken);
        Assert.DoesNotContain("END", spoken.Split('\n'));
        Assert.Contains("Good evening, Night Vale. I'm Cecil Palmer.", spoken);

        var whispered = Speech(script).Single(block => block.Direction == "WHISPERED");
        Assert.Equal("The sky is turning that shade of purple again.", whispered.PlainText);
        var emphasized = Speech(script).SelectMany(block => block.Spans).Where(span => span.IsEmphasized).Select(span => span.Text).ToList();
        Assert.Contains("Instance logs", emphasized);
    }
}
