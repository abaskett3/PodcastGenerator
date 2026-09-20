# Style Guide: "Night Vale"-style narration

Status: DRAFT 1, for the user's review.

## Purpose and scope

This guide defines the tone of the generated podcast: a deadpan, absurdist, fictional community-radio broadcast in the
manner of "Welcome to Night Vale". It is read by the coding agent and is meant to be stored as an editable file that
feeds the narration prompt (Gemini TTS) and the music prompt (Lyria). This is the "researched style guide" of option (A)
in OQ-4 of `docs/specs/initial-development.md`.

What is grounded and what is not:

- Section 1 is grounded in the sources listed at the end.
- Sections 2 to 8 are design decisions written for this project. They are adjustable; they are not claims about how the
  show is produced.
- Section 9 is the only part meant to be sent to a model. Everything else is guidance for people and agents.
- Anything that depends on how OpenRouter passes instructions to the models is unverified until the OQ-9 calls are made.
  Those points are marked **(verify)**.

Hard limits, from the spec ("Out of scope") and `CLAUDE.md`:

- The style is a description of tone only. No text is taken from the show, and no prompt sent to any model quotes it.
- No voice cloning or imitation of any real person, including the show's narrator. Never use `input_references` and
  never name the actor in a prompt. Prompts say "the host".
- The tool narrates the script's words as written. The style guide shapes how the words are delivered, not which words
  are spoken (script rewriting is out of scope for 1.0.0).
- Music prompts describe a sound. They never name an artist (Google blocks prompts requesting specific artist voices
  [S2 in the spec], **(verify)**).

## 1. What the style is (sourced)

- The show is presented as a community radio broadcast from a fictional desert town (Wikipedia).
- The host reports bizarre or supernatural events in a deadpan manner, as if they were mundane local news (Wikipedia).
  One reviewer summed the tone up as "NPR meets cosmic horror" (Wikipedia).
- Humor is surreal and absurdist, with running jokes and long-running plot threads (Wikipedia).
- A typical episode has a cold opening, the main story, a weather report, a sign-off, and a proverb after the credits
  (TV Tropes summary). The weather is a piece of music by a different independent artist each episode (Wikipedia).
- The host mixes news reports with personal thoughts and experiences, and is an unreliable narrator (TV Tropes
  summary). At least one episode is told in the second person (TV Tropes summary).
- Background music is instrumental and atmospheric (Wikipedia names the composer; this guide does not use the name in
  prompts).

## 2. The voice

The host is a calm, warm, slightly formal local radio presenter who is completely at ease. The horror is in the content,
never in the delivery.

- **Deadpan first.** Treat the strange, the bureaucratic and the ominous at the same level. No dramatic reveals, no
  stinger emphasis on scary words.
- **Warm and intimate.** The host speaks to "you", the listener, as a neighbor. Friendly enough that a sudden chill
  lands harder.
- **Unhurried.** Steady, measured, medium-low pitch, clear enunciation. Confident, never rushed, never breathless.
- **Bureaucratic sincerity.** Announcements, notices and corrections are read as if they were important civic business.
- **Occasional softness.** A few lines (an aside, a closing line) may drop to a quieter, more personal register. Use
  sparingly: at most a handful per episode.
- **Not a parody voice.** No exaggerated accent, no spooky vocal effects, no announcer bombast. Neutral, natural
  American English unless the user asks otherwise.

## 3. Pacing and pauses

- Overall tempo: slow to moderate. Slower than conversational speech, without dragging.
- Short declarative sentences and fragments are read with a full stop and a beat. Repetition ("Not three. Six.") gets a
  slight, even slowing, not a rising build.
- Let a line land before the next one starts: add a beat after a punchline, after an ominous fact and before a segment
  change.
- Lists are read evenly, with the same weight on each item.

## 4. Inline audio tags

Google documents inline tags in square brackets, such as `[whispers]`, `[sighs]`, `[laughs]`, `[bored]`,
`[sarcastically]`, `[cough]` and `[gasp]`, placed before the text they affect, and says there is no limit on the tags
that can be used [Google Gemini TTS guides]. The effect of any tag through OpenRouter's `/audio/speech` endpoint is
unverified **(verify, OQ-9)**.

Rules for this project:

- Use tags sparingly. Deadpan is the default; most paragraphs carry none.
- Preferred set, all in the register above: `[whispers]` (quiet asides and the closing line), `[sighs]` (weary
  resignation), `[bored]` or a short free-form descriptor such as `[dryly]` for the flattest lines.
- Never place two bracket tags next to each other. If two qualities are needed, put them in one bracket separated by a
  comma (Google's example combines them this way). This is stricter than the sources require and follows the relayed
  guidance in the spec [S9].
- Direction that is not a delivery tag (for example a script note such as `[Pause]` or an SFX cue) is not passed to the
  speech model as text to be read. Which script markers become tags and which are removed is decided by OQ-2 and OQ-3.
- The narrator must never read direction aloud. Keep direction (profile, scene, director's notes) visibly separate from
  the transcript in the prompt (section 9). Reviewers of Google's guidance report this separation reduces the chance
  that direction is spoken **(verify)**.

## 5. Episode shape

The script controls the actual content. This is the shape the delivery and music should support when the script has
these parts:

1. **Cold opening.** A short greeting in the host's voice, then a strange, calm premise.
2. **Segments.** Each item is a "report": a notice, a correction, a new policy, an announcement. Each begins with a clear
   handoff line ("Now, a matter of...") and ends with a settled closing beat.
3. **Weather.** A break in the news for music. In the audio, this is a natural place for a slightly more prominent
   music passage (how music covers the episode is OQ-8).
4. **Sign-off.** A calm farewell, often with an ominous or oddly comforting last line, sometimes whispered.
5. **Proverb (optional).** A short aphorism after the sign-off, delivered as if profound and slightly off.

## 6. Writing conventions for scripts (for script authors; not applied by the tool)

Use these when writing or asking another model to write a script in this style. The tool narrates the script as given.

- Plain, formal, civic register. Treat anything, however absurd, as routine business.
- Understate the frightening and over-explain the trivial. State impossible facts flatly, then move on.
- Short sentences and fragments for emphasis. A recurring refrain a few times in an episode is fine.
- Address the listener directly ("dear listeners", "you"). Refer to the town and its institutions as familiar.
- Use running jokes and callbacks within an episode, in the show's manner, with original material.
- Let a moment of sincerity or melancholy break the deadpan now and then. It is the contrast that gives the style its
  effect.
- Anchor the source material. When the script is built from a real text (release notes, an article), keep every fact
  accurate. Surreal framing is added around facts, never in place of them.
- Original characters, places and phrases. Do not reproduce show text, catchphrases, or its named characters and
  recurring entities without the user's decision.

## 7. Music (Lyria prompt)

Sound only, no artist names, no lyrics. The music is a bed under a voice, not a foreground track.

- Instrumental, atmospheric, slow, minor key or modal, sparse. Soft synth pads, low drones, muted or detuned piano,
  faint static or tape hiss. Hushed, eerie, gently uneasy, never aggressive or jump-scary.
- Quiet and even in dynamics, no strong lead melody and no sudden loud hits, so the voice stays intelligible over it.
- No vocals, no spoken words, no lyrics **(verify that "instrumental, no vocals" in the prompt is honored, OQ-9)**.
- No genre-specific drum beats or bright pop elements.
- Mix level, fades and tail are OQ-16, not decided here.
- Weather passage (optional, only if OQ-8 allows more than one track): the same palette, slightly more melodic and
  spacious.

## 8. Sound effects

For 1.0.0 the spec recommends that SFX cues are not rendered (OQ-3). If that holds, cues in the script are removed from
the narration and listed on standard error. This guide sets no SFX style until OQ-3 is decided.

## 9. Prompt-ready blocks

These are the only blocks intended to be sent to the models. They contain no text from the show and no real person's
name. Edit the wording here; the code should load it from the style file rather than hardcode it.

### 9.1 Narration prompt template

The structure (Audio Profile, Scene, Director's Notes, Transcript) is the one Google documents for Gemini TTS. Whether
and how this reaches the model through OpenRouter's `input` field is unverified **(verify, OQ-9)**.

```
# AUDIO PROFILE: The Host
## A late-night community radio presenter for a small, strange desert town.

## THE SCENE
A small, dim radio booth after dark. A single lamp, a microphone, a faint hum of equipment. The host reads the evening's
announcements to the town with total calm, as if this were the most ordinary broadcast in the world.

### DIRECTOR'S NOTES
Style: Deadpan and warm. The host is completely at ease and treats strange, ominous or bureaucratic news as routine
civic business. No dramatic emphasis on frightening words, no spooky effects, no announcer bombast. Intimate, as if
speaking to a neighbor. Occasional quiet, more personal asides.
Pacing: Slow to moderate, steady and unhurried. A beat of silence after short declarative lines and before a change of
subject. Lists are read evenly.
Accent: Neutral, natural American English, no exaggerated regional features.

#### TRANSCRIPT
{script text, with only the tags allowed by section 4}
```

Notes for the implementation:

- The Director's Notes must be identical on every request in a run. Google's speech-generation docs say quality and
  consistency can drift on outputs longer than a few minutes and recommend splitting transcripts into smaller chunks.
  If OQ-10 leads to chunking, resend the same profile, scene and notes with each chunk to keep the voice consistent
  (design inference, **verify** by listening).
- Google's guidance also says not to overspecify and to leave the model room. Do not lengthen the notes with more
  adjectives without listening to the result.
- Voice selection (OQ-5): pick a calm, warm, mid-to-low preset by listening. Do not choose or describe a voice by
  resemblance to a real person.

### 9.2 Music prompt

```
Instrumental background music for a late-night community radio broadcast from a strange desert town. Slow, sparse,
atmospheric, in a minor key. Soft synth pads, a low drone, muted detuned piano, faint tape hiss and radio static.
Hushed, eerie, gently uneasy, but calm. Quiet and even in dynamics with no strong lead melody and no sudden loud hits,
so it works quietly under a spoken voice. No vocals, no lyrics.
```

## Sources

Read by the author of this guide on 2026-09-19. Web pages were retrieved through a summarizing fetch tool, not read
verbatim, so the coding agent should read them itself before relying on them.

- Wikipedia, Welcome to Night Vale: https://en.wikipedia.org/wiki/Welcome_to_Night_Vale
- TV Tropes, Welcome to Night Vale (via search summary; the page itself returned HTTP 403):
  https://tvtropes.org/pmwiki/pmwiki.php/Podcast/WelcomeToNightVale
- Google AI Studio, Gemini 3.1 TTS prompt guide (via search summary; page did not load):
  https://aistudio.google.com/learn/gemini-tts-prompt-guide-with-tags
- Mirror of the same guide, read: https://dev.to/googleai/how-to-prompt-gemini-31s-new-text-to-speech-model-24bb
- Google, Gemini speech generation docs, read: https://ai.google.dev/gemini-api/docs/speech-generation (this is the
  general Gemini TTS page; it may not be specific to `gemini-3.1-flash-tts-preview`)
- `docs/specs/initial-development.md` (OQ-2, 3, 4, 5, 8, 9, 10, 16; [S2], [S9]) and
  `docs/sample-scripts/QE-3395-RC-09-17-26-nightvale-podcast-script.txt`, read directly.

## Open items for the user

1. **Host name in scripts.** The sample script uses the show's character name as the speaker label. This guide's prompts
   say "the host" and never name the character or the actor. Whether scripts should use an original host name is
   your call.
2. **Accent.** Section 2 defaults to neutral American English. Change it in section 9.1 if you want otherwise.
3. **Script rewriting.** Section 6 is guidance for authoring scripts; the tool does not rewrite text. If you want
   "any text file, rewritten in this style" as a product feature, that is a spec change (it is out of scope in the
   current spec).
