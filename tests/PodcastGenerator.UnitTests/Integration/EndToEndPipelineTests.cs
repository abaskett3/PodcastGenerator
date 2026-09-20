using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PodcastGenerator.Application.Narration;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>Integration tests: the real dependency injection wiring, the real file system, the real HTTP client pipeline
/// (behind a fake handler), the real MP3 encoder and the real CLI runner. No test here reaches the network or reads the
/// user's key or profile folders (AC-5). Expected values come from the spec, the script guide and the documented API, not
/// from what the code happens to produce.</summary>
public sealed class EndToEndPipelineTests : IDisposable
{
    private static readonly string SamplePath = RepositoryFiles.Combine("docs", "sample-scripts", "QE-3395-RC-09-17-26-nightvale-podcast-script.txt");

    private readonly Pipeline _pipeline = new();

    public void Dispose() => _pipeline.Dispose();

    private static string SampleText => File.ReadAllText(SamplePath);

    private static string[] StderrLines(Pipeline pipeline) =>
        pipeline.Err.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    // AC-7, AC-8, AC-9, AC-51, AC-52, AC-53: the whole sample script through every real layer.
    [Fact]
    public async Task The_sample_script_becomes_one_valid_two_channel_mp3_in_the_default_directory()
    {
        var exitCode = await _pipeline.RunAsync(SamplePath);

        Assert.Equal(0, exitCode);
        Assert.Equal(_pipeline.DefaultOutputPath + Environment.NewLine, _pipeline.Out.ToString());
        Assert.Equal(["Podcast-09-19-2026.mp3"], Pipeline.Entries(_pipeline.DefaultOutputDirectory));

        var chunkCount = _pipeline.Http.Requests.Count;
        Assert.True(chunkCount > 1, "The sample is longer than one chunk, so it needs more than one request.");

        // AC-51: progress on standard error, "chunk k of n", with n known from the first line.
        var progress = StderrLines(_pipeline).Where(line => Regex.IsMatch(line, @"^chunk \d+ of \d+$")).ToList();
        Assert.Equal(Enumerable.Range(1, chunkCount).Select(k => $"chunk {k} of {chunkCount}"), progress);
        Assert.Equal($"chunk 1 of {chunkCount}", StderrLines(_pipeline)[0]);

        // AC-53 and AC-52: decode the file.
        var mp3 = DecodedMp3.Read(_pipeline.DefaultOutputPath);
        Assert.Equal(2, mp3.Channels);
        Assert.Equal(24000, mp3.SampleRate);
        Assert.True(mp3.Left.SequenceEqual(mp3.Right), "The two channels must carry identical samples (centered, no panning).");

        // Each stubbed response is 0.5 s. The duration is the sum within the design doc's stated tolerance of 100 ms, and no
        // silence is added between chunks.
        Assert.InRange(mp3.Seconds, 0.5 * chunkCount, (0.5 * chunkCount) + 0.1);

        // AC-52: no chunk missing, duplicated or reordered. Chunk k was answered with a tone of 300 + 200(k-1) Hz.
        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            var expectedFrequency = 300 + (200 * chunk);
            var crossings = mp3.ZeroCrossings((0.5 * chunk) + 0.1, (0.5 * chunk) + 0.4);
            var expected = 2 * expectedFrequency * 0.3;
            Assert.InRange(crossings, expected * 0.9, expected * 1.1);
        }
    }

    // AC-40, AC-41, AC-26: what goes over the wire.
    [Fact]
    public async Task Every_request_is_a_documented_POST_with_the_fixed_model_and_voice_and_the_key_only_in_the_header()
    {
        await _pipeline.RunAsync(SamplePath);

        Assert.NotEmpty(_pipeline.Http.Requests);
        foreach (var request in _pipeline.Http.Requests)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://openrouter.ai/api/v1/audio/speech", request.Uri!.ToString());
            Assert.Equal("Bearer", request.AuthorizationScheme);
            Assert.Equal(TestKeys.Sentinel, request.AuthorizationParameter);
            Assert.Equal("application/json", request.ContentType);
            Assert.DoesNotContain(TestKeys.Sentinel, request.Body);

            // Only fields documented at https://openrouter.ai/docs/guides/overview/multimodal/tts, none that clones a voice.
            var fields = request.Json.EnumerateObject().Select(property => property.Name).ToHashSet();
            Assert.Subset(new HashSet<string> { "model", "input", "voice", "response_format", "speed", "input_references", "provider" }, fields);
            Assert.Contains("model", fields);
            Assert.Contains("input", fields);
            Assert.Contains("voice", fields);
            Assert.DoesNotContain("input_references", fields);
            Assert.Equal("google/gemini-3.1-flash-tts-preview", request.Json.GetProperty("model").GetString());
            Assert.Equal("Umbriel", request.Json.GetProperty("voice").GetString());
            Assert.Contains(request.Json.GetProperty("response_format").GetString(), new[] { "mp3", "pcm" });
        }
    }

    // AC-50: same profile, scene and notes, voice and format on every chunk, one request at a time, in script order.
    [Fact]
    public async Task Every_chunk_carries_the_same_direction_voice_and_format_and_chunks_are_requested_one_at_a_time()
    {
        await _pipeline.RunAsync(SamplePath);

        var requests = _pipeline.Http.Requests;
        Assert.Equal(1, _pipeline.Http.MaxConcurrent);
        Assert.Single(requests.Select(request => request.Direction).Distinct());
        Assert.Single(requests.Select(request => request.Json.GetProperty("voice").GetString()).Distinct());
        Assert.Single(requests.Select(request => request.Json.GetProperty("response_format").GetString()).Distinct());
    }

    // AC-45, AC-47, AC-59: the direction is the style guide's section 9.1 structure and is visibly separate from the transcript.
    [Fact]
    public async Task The_request_has_profile_scene_notes_then_the_transcript_and_the_direction_names_no_person_or_music()
    {
        await _pipeline.RunAsync(SamplePath);

        var first = _pipeline.Http.Requests[0];
        var input = first.Input;
        var profile = input.IndexOf("AUDIO PROFILE", StringComparison.Ordinal);
        var scene = input.IndexOf("THE SCENE", StringComparison.Ordinal);
        var notes = input.IndexOf("DIRECTOR'S NOTES", StringComparison.Ordinal);
        var transcript = input.IndexOf("#### TRANSCRIPT", StringComparison.Ordinal);
        Assert.True(profile >= 0 && profile < scene && scene < notes && notes < transcript, "Order must be profile, scene, notes, transcript.");

        var direction = first.Direction;
        foreach (var forbidden in new[] { "Baldwin", "Night Vale", "Cecil", "Lyria", "music", "mixing", "sound effect" })
        {
            Assert.DoesNotContain(forbidden, direction, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("American", direction, StringComparison.Ordinal);
    }

    // AC-28 to AC-38: golden test on the sample, read from the requests that were sent.
    [Fact]
    public async Task The_text_sent_for_the_sample_has_no_headings_headers_speaker_names_cues_END_or_asterisks()
    {
        await _pipeline.RunAsync(SamplePath);

        var transcripts = _pipeline.Http.Requests.Select(request => request.Transcript).ToList();
        var all = string.Join("\n\n", transcripts);

        foreach (var forbidden in new[]
                 {
                     "PROGRAMME", "EPISODE", "STYLE:", "CAST:", "SCENE 1", "SEGMENT", "WELCOME TO VAST NIGHT VALE", "CECIL:",
                     "CECIL PALMER (host)", "CONT'D", "[SFX", "[MUSIC", "[FADE", "*", "(BEAT)", "(WHISPERED)", "#",
                 })
        {
            Assert.DoesNotContain(forbidden, all, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(all.Split('\n'), line => line.Trim() == "END");
        Assert.Contains("Good evening, Night Vale. I'm Cecil Palmer.", all, StringComparison.Ordinal);

        // A delivery tag immediately precedes the whispered line, and an emphasis tag precedes "Instance logs" (line 27).
        Assert.Matches(@"\[[^\[\]]+\] The sky is turning that shade of purple again\.", all);
        Assert.Matches(@"\[[^\[\]]+\] Instance logs", all);
        Assert.DoesNotContain("WHISPERED", all, StringComparison.Ordinal);

        // AC-33: the two (BEAT) lines become pause tags, and no words of the pause are spoken.
        var tags = Regex.Matches(all, @"\[[^\]]*\]").Select(match => match.Value).ToList();
        Assert.Contains(tags, tag => tag.Contains("pause", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("BEAT", all, StringComparison.Ordinal);

        // AC-37: never two tags next to each other.
        Assert.DoesNotMatch(@"\]\s*\[", all);
    }

    // AC-30, AC-36, AC-48, AC-52: with the tags removed, the requests together say exactly the script's spoken paragraphs, in order.
    [Fact]
    public async Task The_requests_together_carry_every_spoken_paragraph_once_in_script_order_and_nothing_else()
    {
        await _pipeline.RunAsync(SamplePath);

        var expected = ScriptOracle.Paragraphs(SampleText);
        var sentParagraphs = _pipeline.Http.Requests
            .SelectMany(request => request.Transcript.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            .Select(ScriptOracle.WithoutTags)
            .Where(paragraph => paragraph.Length > 0)
            .ToList();

        Assert.Equal(expected.Select(ScriptOracle.Normalize), sentParagraphs);
    }

    // AC-48: chunk size limit, and a segment that fits is not split across chunks.
    [Fact]
    public async Task No_chunk_is_larger_than_the_chunk_size_and_no_segment_that_fits_is_split()
    {
        await _pipeline.RunAsync(SamplePath);

        var transcripts = _pipeline.Http.Requests.Select(request => request.Transcript).ToList();
        Assert.All(transcripts, transcript => Assert.True(transcript.Length <= NarrationSettings.MaxChunkCharacters, $"A chunk has {transcript.Length} characters."));

        // AC-48, D-9: a tag is never left alone at the end of a chunk or as a paragraph of its own; it goes with its text.
        Assert.All(transcripts, transcript =>
        {
            Assert.DoesNotMatch(@"\]\s*$", transcript);
            Assert.DoesNotContain(transcript.Split("\n\n"), paragraph => Regex.IsMatch(paragraph.Trim(), @"^\[[^\]]*\]$"));
        });

        var segments = ScriptOracle.Segments(SampleText);
        foreach (var segment in segments)
        {
            // A margin of 100 characters covers the tags added to a segment.
            var size = segment.Sum(paragraph => paragraph.Length) + (2 * (segment.Count - 1));
            if (size + 100 > NarrationSettings.MaxChunkCharacters)
            {
                continue;
            }

            var firstParagraph = ScriptOracle.Normalize(segment[0]);
            var lastParagraph = ScriptOracle.Normalize(segment[^1]);
            var start = transcripts.FindIndex(t => t.Split("\n\n").Any(p => ScriptOracle.WithoutTags(p) == firstParagraph));
            var end = transcripts.FindIndex(t => t.Split("\n\n").Any(p => ScriptOracle.WithoutTags(p) == lastParagraph));
            Assert.True(start >= 0 && start == end, $"A segment of {size} characters must be in one chunk (starts in {start}, ends in {end}).");
        }
    }

    // AC-36: the same script gives the same request text on every run, whatever the line endings and byte order mark.
    [Fact]
    public async Task The_same_script_gives_the_same_request_text_with_LF_or_CRLF_and_with_or_without_a_byte_order_mark()
    {
        await _pipeline.RunAsync(SamplePath);
        var baseline = _pipeline.Http.Requests.Select(request => request.Body).ToList();

        var crlf = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(SampleText.Replace("\r\n", "\n").Replace("\n", "\r\n"))).ToArray();
        var scriptPath = _pipeline.WriteScriptBytes(crlf, "crlf-bom.txt");
        var again = new Pipeline();
        try
        {
            again.WriteKeyFile($"OPENROUTER_API_KEY={TestKeys.Sentinel}\n");
            var exitCode = await again.RunAsync(scriptPath);

            Assert.Equal(0, exitCode);
            Assert.Equal(baseline, again.Http.Requests.Select(request => request.Body).ToList());
        }
        finally
        {
            again.Dispose();
        }
    }

    // AC-34: cues are removed silently.
    [Fact]
    public async Task Cue_lines_are_never_printed_or_sent()
    {
        await _pipeline.RunAsync(SamplePath);

        var cues = SampleText.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("[SFX", StringComparison.Ordinal) || line.StartsWith("[MUSIC", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(cues);
        var printed = _pipeline.Out + _pipeline.Err.ToString();
        var sent = string.Join("\n", _pipeline.Http.Requests.Select(request => request.Transcript));
        foreach (var cue in cues)
        {
            var inner = cue.Trim('[', ']');
            Assert.DoesNotContain(inner, printed, StringComparison.Ordinal);
            Assert.DoesNotContain(inner, sent, StringComparison.Ordinal);
        }
    }

    // AC-32, AC-33, AC-35, AC-37: a pause, a direction and an emphasis together end up in one tag, before the words.
    [Fact]
    public async Task A_pause_a_direction_and_an_emphasis_in_a_row_become_one_tag_before_the_words()
    {
        var script = _pipeline.WriteScript("CECIL: First line.\n\n(BEAT)\n\nCECIL: (WHISPERED) *Quiet* now.\n");

        await _pipeline.RunAsync(script);

        var transcript = Assert.Single(_pipeline.Http.Requests).Transcript;
        var paragraphs = transcript.Split("\n\n");
        Assert.Equal(2, paragraphs.Length);
        Assert.Equal("First line.", paragraphs[0]);
        Assert.Matches(@"^\[[^\[\]]+\] Quiet now\.$", paragraphs[1]);
        Assert.DoesNotMatch(@"\]\s*\[", transcript);
        Assert.DoesNotContain("*", transcript, StringComparison.Ordinal);
    }

    // AC-48: a segment is the first split boundary.
    [Fact]
    public async Task Segments_that_do_not_fit_together_are_split_at_the_segment_boundary()
    {
        // Each segment is about 0.6 of a chunk, so two never fit together and one always fits alone.
        var paragraph = string.Concat(Enumerable.Repeat("Word ", (int)(NarrationSettings.MaxChunkCharacters * 0.6 / 5)));
        var script = new StringBuilder("# T\n\n## SCENE 1 - INT. STUDIO - NIGHT\n\n");
        foreach (var name in new[] { "Alpha", "Beta", "Gamma" })
        {
            script.Append($"### SEGMENT {name}\n\nCECIL: {name}Marker {paragraph.Trim()}\n\n");
        }

        var path = _pipeline.WriteScript(script.ToString());
        await _pipeline.RunAsync(path);

        var transcripts = _pipeline.Http.Requests.Select(request => request.Transcript).ToList();
        Assert.Equal(3, transcripts.Count);
        Assert.StartsWith("AlphaMarker", transcripts[0], StringComparison.Ordinal);
        Assert.StartsWith("BetaMarker", transcripts[1], StringComparison.Ordinal);
        Assert.StartsWith("GammaMarker", transcripts[2], StringComparison.Ordinal);
    }

    // AC-48: a segment larger than a chunk is split at paragraph boundaries.
    [Fact]
    public async Task A_segment_larger_than_a_chunk_is_split_at_paragraph_boundaries()
    {
        var script = new StringBuilder("### SEGMENT 1 - BIG\n\n");
        var paragraphs = new List<string>();
        for (var index = 1; index <= 8; index++)
        {
            var text = $"Para{index:D2} " + string.Concat(Enumerable.Repeat("word ", 60)).Trim() + ".";
            paragraphs.Add(text);
            script.Append(index == 1 ? $"CECIL: {text}\n\n" : $"{text}\n\n");
        }

        Assert.True(paragraphs.Sum(paragraph => paragraph.Length) > NarrationSettings.MaxChunkCharacters);
        var path = _pipeline.WriteScript(script.ToString());
        await _pipeline.RunAsync(path);

        var sent = _pipeline.Http.Requests.SelectMany(request => request.Transcript.Split("\n\n")).ToList();
        Assert.True(_pipeline.Http.Requests.Count > 1);
        Assert.Equal(paragraphs, sent);
    }

    // AC-48: an oversized paragraph is split at sentence ends, with a warning, and a delivery tag stays with its text.
    [Fact]
    public async Task An_oversized_paragraph_is_split_at_sentence_ends_with_a_warning_and_the_tag_stays_with_its_text()
    {
        var sentences = Enumerable.Range(1, 60).Select(index => $"Sentence {index:D2} is a short and plain statement of fact.").ToList();
        var path = _pipeline.WriteScript("CECIL: (WHISPERED) " + string.Join(' ', sentences) + "\n");
        Assert.True(string.Join(' ', sentences).Length > NarrationSettings.MaxChunkCharacters);

        var exitCode = await _pipeline.RunAsync(path);

        Assert.Equal(0, exitCode);
        var transcripts = _pipeline.Http.Requests.Select(request => request.Transcript).ToList();
        Assert.True(transcripts.Count > 1);
        Assert.Contains(StderrLines(_pipeline), line => line.StartsWith("Warning:", StringComparison.Ordinal));
        Assert.All(transcripts, transcript =>
        {
            Assert.True(transcript.Length <= NarrationSettings.MaxChunkCharacters);
            Assert.EndsWith(".", transcript, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\]\s*$", transcript);
        });

        // The tag is followed by its text, never the end of a chunk.
        Assert.Matches(@"^\[[^\[\]]+\] Sentence 01 ", transcripts[0]);

        // Nothing lost, nothing repeated, in order.
        var joined = ScriptOracle.WithoutTags(string.Join(' ', transcripts));
        Assert.Equal(string.Join(' ', sentences), joined);
    }

    // AC-48, D-11: a sentence longer than a whole chunk cannot be split at a sentence end; it is sent whole with a warning.
    [Fact]
    public async Task A_single_sentence_longer_than_a_chunk_is_sent_whole_with_a_warning_and_nothing_is_lost()
    {
        var sentence = "It goes on " + string.Concat(Enumerable.Repeat("and on ", (NarrationSettings.MaxChunkCharacters / 7) + 50)) + "forever.";
        var path = _pipeline.WriteScript($"CECIL: {sentence}\n");
        Assert.True(sentence.Length > NarrationSettings.MaxChunkCharacters);

        var exitCode = await _pipeline.RunAsync(path);

        Assert.Equal(0, exitCode);
        Assert.Equal(sentence, Assert.Single(_pipeline.Http.Requests).Transcript);
        Assert.Contains(StderrLines(_pipeline), line => line.StartsWith("Warning:", StringComparison.Ordinal));
    }

    // AC-21, D-21: a script of 3,000 lines is processed to completion with no line-count limit.
    [Fact]
    public async Task A_script_of_three_thousand_lines_is_processed_to_completion()
    {
        var lines = new List<string> { "# LONG EPISODE", string.Empty, "PROGRAMME: Long", string.Empty, "## SCENE 1 - INT. STUDIO - NIGHT", string.Empty };
        var marker = 0;
        while (lines.Count < 3000)
        {
            if (marker % 40 == 0)
            {
                lines.Add($"### SEGMENT {(marker / 40) + 1} - PART");
                lines.Add(string.Empty);
            }

            marker++;
            lines.Add(marker == 1 ? $"CECIL: Lx{marker:D5} is a plain line of spoken copy." : $"Lx{marker:D5} is a plain line of spoken copy.");
            lines.Add(string.Empty);
        }

        var text = string.Join('\n', lines.Take(3000));
        Assert.Equal(3000, text.Split('\n').Length);
        var speechLineCount = Regex.Matches(text, @"Lx\d{5}").Count;
        _pipeline.Http.Responder = _ => CannedSpeechHandler.Audio(Pcm.Silence(0.05));
        var path = _pipeline.WriteScript(text);

        var exitCode = await _pipeline.RunAsync(path);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(_pipeline.DefaultOutputPath));
        Assert.Equal(1, _pipeline.Http.MaxConcurrent);
        var sentMarkers = _pipeline.Http.Requests
            .SelectMany(request => Regex.Matches(request.Transcript, @"Lx\d{5}").Select(match => match.Value))
            .ToList();
        Assert.Equal(Enumerable.Range(1, speechLineCount).Select(number => $"Lx{number:D5}"), sentMarkers);
        Assert.All(_pipeline.Http.Requests, request => Assert.True(request.Transcript.Length <= NarrationSettings.MaxChunkCharacters));
        var mp3 = DecodedMp3.Read(_pipeline.DefaultOutputPath);
        Assert.Equal(2, mp3.Channels);
        Assert.InRange(mp3.Seconds, 0.05 * _pipeline.Http.Requests.Count, (0.05 * _pipeline.Http.Requests.Count) + 0.1);
    }

    // The Unicode characters of the sample (an em dash in "request-just") survive JSON encoding.
    [Fact]
    public async Task Non_ASCII_characters_reach_the_request_body_unchanged()
    {
        var path = _pipeline.WriteScript("CECIL: One caller could construct a request—just a simple one. Café “quoted”.\n");

        await _pipeline.RunAsync(path);

        var transcript = Assert.Single(_pipeline.Http.Requests).Transcript;
        Assert.Equal("One caller could construct a request—just a simple one. Café “quoted”.", transcript);
        using var _ = JsonDocument.Parse(_pipeline.Http.Requests[0].Body);
    }
}
