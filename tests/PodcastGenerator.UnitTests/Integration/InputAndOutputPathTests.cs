using System.Text;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>Script and output path handling through the real file system (AC-9 to AC-20). Every run uses stubbed HTTP and a
/// temporary profile folder.</summary>
public sealed class InputAndOutputPathTests : IDisposable
{
    private readonly Pipeline _pipeline = new();

    public void Dispose() => _pipeline.Dispose();

    private string OneLineScript(string fileName = "episode.txt") => _pipeline.WriteScript("CECIL: Good evening.\n", fileName);

    private string Out(params string[] parts) => Path.Combine([_pipeline.Root, "out", .. parts]);

    // AC-10: the default name gets -2, then -3, and an existing file is never modified.
    [Fact]
    public async Task An_existing_default_file_is_never_touched_and_the_next_runs_get_suffix_2_then_3()
    {
        Directory.CreateDirectory(_pipeline.DefaultOutputDirectory);
        File.WriteAllText(_pipeline.DefaultOutputPath, "an earlier episode");
        var script = OneLineScript();

        var first = await _pipeline.RunAsync(script);
        var second = await _pipeline.RunAsync(script);

        Assert.Equal(0, first);
        Assert.Equal(0, second);
        Assert.Equal("an earlier episode", File.ReadAllText(_pipeline.DefaultOutputPath));
        Assert.Equal(
            ["Podcast-09-19-2026-2.mp3", "Podcast-09-19-2026-3.mp3", "Podcast-09-19-2026.mp3"],
            Pipeline.Entries(_pipeline.DefaultOutputDirectory));
        var printed = _pipeline.Out.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            [Path.Combine(_pipeline.DefaultOutputDirectory, "Podcast-09-19-2026-2.mp3"), Path.Combine(_pipeline.DefaultOutputDirectory, "Podcast-09-19-2026-3.mp3")],
            printed);
    }

    // AC-9: the local date of the run, MM-DD-YYYY, with the real clock.
    [Fact]
    public async Task The_default_name_uses_the_local_date_of_the_run_as_month_day_year()
    {
        _pipeline.Clock = TimeProvider.System;
        var before = DateTime.Now;

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        var after = DateTime.Now;
        Assert.Equal(0, exitCode);
        var name = Assert.Single(Pipeline.Entries(_pipeline.DefaultOutputDirectory));
        Assert.Contains(name, new[] { $"Podcast-{before:MM-dd-yyyy}.mp3", $"Podcast-{after:MM-dd-yyyy}.mp3" });
    }

    // AC-11: an explicit file path, with missing parent directories created.
    [Fact]
    public async Task An_explicit_file_path_is_written_and_missing_parent_directories_are_created()
    {
        var target = Out("a", "b", "show.mp3");

        var exitCode = await _pipeline.RunAsync(OneLineScript(), target);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(target));
        Assert.Equal(target + Environment.NewLine, _pipeline.Out.ToString());
        Assert.Equal(["show.mp3"], Pipeline.Entries(Out("a", "b")));
        Assert.False(Directory.Exists(_pipeline.DefaultOutputDirectory));
    }

    // AC-10: also for an explicit output path.
    [Fact]
    public async Task An_existing_explicit_file_is_never_overwritten_and_the_next_runs_get_suffix_2_then_3()
    {
        Directory.CreateDirectory(Out());
        var target = Out("show.mp3");
        File.WriteAllText(target, "an earlier episode");
        var script = OneLineScript();

        await _pipeline.RunAsync(script, target);
        await _pipeline.RunAsync(script, target);

        Assert.Equal("an earlier episode", File.ReadAllText(target));
        Assert.Equal(["show-2.mp3", "show-3.mp3", "show.mp3"], Pipeline.Entries(Out()));
    }

    // AC-12: an existing directory receives the default file name.
    [Fact]
    public async Task An_existing_directory_receives_the_file_with_the_default_name()
    {
        Directory.CreateDirectory(Out("albums"));

        var exitCode = await _pipeline.RunAsync(OneLineScript(), Out("albums"));

        Assert.Equal(0, exitCode);
        Assert.Equal(["Podcast-09-19-2026.mp3"], Pipeline.Entries(Out("albums")));
        Assert.False(Directory.Exists(_pipeline.DefaultOutputDirectory));
    }

    // AC-13: a file path that is not .mp3 is an error naming .mp3, with no request and no directory created.
    [Theory]
    [InlineData("show.wav")]
    [InlineData("show.txt")]
    [InlineData("show.mp4")]
    public async Task An_output_file_that_is_not_mp3_is_an_error_naming_mp3_with_no_request_and_nothing_created(string fileName)
    {
        var target = Out("deep", fileName);

        var exitCode = await _pipeline.RunAsync(OneLineScript(), target);

        Assert.Equal(1, exitCode);
        Assert.Empty(_pipeline.Http.Requests);
        Assert.Contains(".mp3", _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Empty(_pipeline.Out.ToString());
        Assert.False(Directory.Exists(Out("deep")));
        Assert.False(Directory.Exists(_pipeline.DefaultOutputDirectory));
    }

    // AC-18: a missing script names the path.
    [Fact]
    public async Task A_missing_script_names_the_path_and_makes_no_request()
    {
        var missing = Path.Combine(_pipeline.ScriptDirectory, "not-there.txt");

        var exitCode = await _pipeline.RunAsync(missing);

        Assert.Equal(1, exitCode);
        Assert.Empty(_pipeline.Http.Requests);
        Assert.Contains(missing, _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Empty(_pipeline.Out.ToString());
    }

    // AC-19: only .txt, matched case-insensitively.
    [Theory]
    [InlineData("script.md")]
    [InlineData("script.markdown")]
    [InlineData("script.docx")]
    [InlineData("script")]
    public async Task Any_extension_other_than_txt_is_rejected_saying_only_txt_is_supported(string fileName)
    {
        var path = _pipeline.WriteScript("CECIL: Good evening.\n", fileName);

        var exitCode = await _pipeline.RunAsync(path);

        Assert.Equal(1, exitCode);
        Assert.Empty(_pipeline.Http.Requests);
        Assert.Contains(".txt", _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Empty(_pipeline.Out.ToString());
        Assert.False(Directory.Exists(_pipeline.DefaultOutputDirectory));
    }

    [Theory]
    [InlineData("SCRIPT.TXT")]
    [InlineData("script.Txt")]
    public async Task An_upper_case_txt_extension_is_accepted(string fileName)
    {
        var exitCode = await _pipeline.RunAsync(OneLineScript(fileName));

        Assert.Equal(0, exitCode);
        Assert.Single(_pipeline.Http.Requests);
    }

    // AC-20: an empty script, or one with nothing narratable, is rejected: nothing to narrate, no request.
    [Theory]
    [InlineData("")]
    [InlineData("\n\n   \n")]
    [InlineData("# TITLE\n\nPROGRAMME: Show\n\nEPISODE: One\n\nSTYLE: x\n\nCAST: CECIL PALMER (host)\n\n## SCENE 1 - INT. STUDIO - NIGHT\n\n### SEGMENT 1 - OPENING\n\nEND\n")]
    [InlineData("[MUSIC: THEME - EERIE (FADE UP)]\n\n[SFX: STATIC]\n\n(BEAT)\n\n(PAUSE - 3 SECONDS)\n\n[FADE OUT]\n\nEND\n")]
    public async Task An_empty_script_or_one_with_only_cues_is_rejected_saying_there_is_nothing_to_narrate(string script)
    {
        var path = _pipeline.WriteScript(script);

        var exitCode = await _pipeline.RunAsync(path);

        Assert.Equal(1, exitCode);
        Assert.Empty(_pipeline.Http.Requests);
        Assert.Contains("nothing to narrate", _pipeline.Err.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_pipeline.Out.ToString());
        Assert.False(File.Exists(_pipeline.DefaultOutputPath));
    }

    // AC-36: a script that departs from the guide (no speaker labels, no headings) still runs and is narrated as written.
    [Fact]
    public async Task A_script_with_no_speaker_labels_or_headings_is_narrated_as_written()
    {
        var path = _pipeline.WriteScript("Just a plain paragraph of words.\n\nAnd another one.\n");

        var exitCode = await _pipeline.RunAsync(path);

        Assert.Equal(0, exitCode);
        Assert.Equal("Just a plain paragraph of words.\n\nAnd another one.", Assert.Single(_pipeline.Http.Requests).Transcript);
    }

    // D-16: UTF-8 with a byte order mark and CRLF line endings.
    [Fact]
    public async Task A_script_with_a_byte_order_mark_and_CRLF_line_endings_is_read_correctly()
    {
        var text = "﻿# TITLE\r\n\r\nPROGRAMME: Show\r\n\r\n## SCENE 1 - INT. STUDIO - NIGHT\r\n\r\nCECIL: Good evening.\r\n\r\nSecond paragraph.\r\n";
        var path = _pipeline.WriteScriptBytes(new UTF8Encoding(false).GetBytes(text));

        var exitCode = await _pipeline.RunAsync(path);

        Assert.Equal(0, exitCode);
        Assert.Equal("Good evening.\n\nSecond paragraph.", Assert.Single(_pipeline.Http.Requests).Transcript);
    }
}
