# Script Writing Guide

Status: DRAFT 1.

## Purpose and scope

This guide tells agents how a podcast or audio script is formatted and written, so any agent that writes, edits or
checks a script applies the same rules. It covers **format** (how speakers, cues, directions and headings are marked up)
and **spoken-copy standards** (how the words are written so they can be read aloud).

It does not cover tone, voice or style of a particular show.

Where the sources disagree, this guide picks one convention and says so under "House choices". Those choices are
adjustable. Everything else is drawn from the sources at the end.

## Quick reference

| Element | Format | Example |
|---|---|---|
| Header | `LABEL: value` lines at the top | `PROGRAMME: Welcome to Vast Night Vale` |
| Scene heading | Caps: `SCENE n – INT./EXT. LOCATION – TIME` | `SCENE 1 – INT. RADIO STUDIO – EVENING` |
| Segment heading | Caps: `SEGMENT n – TITLE`. Production label, never spoken | `SEGMENT 2 – INSTANCE LOGS` |
| Speaker | Name in caps, colon, then the line | `CECIL: Good evening, Night Vale.` |
| Delivery direction | Caps, in parentheses, first thing after the speaker name | `CECIL: (WHISPERED) The sky is turning purple.` |
| Sound effect | Caps, square brackets, own line, starts `SFX:` | `[SFX: SOFT RADIO STATIC]` |
| Music | Caps, square brackets, own line, starts `MUSIC:` | `[MUSIC: LOW, OMINOUS (UNDER)]` |
| Fade | Caps, in parentheses, at the end of a cue | `[SFX: STATIC - FADE OUT]` |
| Pause | Caps, parentheses, own line | `(BEAT)` or `(PAUSE - 3 SECONDS)` |
| Continued speech | `NAME: (CONT'D)` after a cue interrupts the speaker | `CECIL: (CONT'D) We have corrected this.` |
| Transition | Caps, square brackets, own line | `[FADE OUT]` |

## Layout rules

1. One element per paragraph. Leave a blank line between every element, so a cue is never merged into the line before
   it. Scripts are plain `.txt` files.
2. A cue is never part of a spoken line. Sound, music and fades go on their own line, never inside a speech.
3. A delivery direction sits at the start of the line it applies to, straight after the speaker name. It is never
   placed mid-sentence and never folded into the dialogue text. This reduces the chance it is read aloud.
4. When a cue interrupts a speech, the speaker resumes with `NAME: (CONT'D)`.
5. Speaker names are consistent. Introduce a character with the full name in the cast line; shortened after that, but
   always capitalized.
6. Cues are specific enough that the person building the mix does not have to guess. Write what is heard, not a mood
   word alone. Layered sounds go in one cue, separated by commas.
7. Use music sparingly. Music is indicated when it is a source (heard in the scene) or has a function, such as a
   transition between scenes or segments. Do not add music cues as decoration.
8. Use the standard music terms where they fit: `BRIDGE` (short transition), `STING` (brief accent), `BED` (sustained
   background), `UNDER` (continues quietly beneath speech), `OUT` (ends completely).
9. Use the standard fade terms: `(FADE UP)`, `(FADE DOWN)`, `(FADE UNDER)`, `(FADE OUT)`.

## Script skeleton

```text
# TITLE

PROGRAMME: Title of the show

EPISODE: Episode name or number

CAST: FULL NAME (role)

## SCENE 1 – INT. LOCATION – TIME

### SEGMENT 1 – OPENING

[MUSIC: THEME – DESCRIPTION OF THE SOUND (FADE UP, THEN UNDER)]

HOST: First line of speech.

Second paragraph of the same speech.

[SFX: WHAT THE LISTENER HEARS]

HOST: (CONT'D) The speech resumes.

(BEAT)

HOST: (WHISPERED) A line with a delivery direction.

[FADE OUT]

END
```

`#`, `##` and `###` are plain-text stand-ins for the centered, underlined headings of a printed script. They are not
spoken.

## Writing for the ear

The script is heard, not read. These rules come from broadcast writing guides.

- Write the way people talk. Use contractions ("don't", not "do not").
- Keep sentences to 20 words or fewer, one thought each. The reader has to breathe.
- Do not use semicolons. Use a dash for a pause longer than a comma. (The sources use a double dash; the em dash is the
  typeset equivalent.)
- Numbers: spell out zero to eleven. Use numerals for 12 to 999. Write larger figures as words, and hyphenate
  combinations of numerals and words. Exceptions: a number that is a joke or an emphasis beat is written as spoken
  ("Three. Point. Two. Two."), and codes are written as said ("a five-hundred error").
- Avoid symbols. Write "dollar", not `$`. Write "dot", "slash" and "percent" as words if they must be said at all.
- Avoid abbreviations, even on second reference, unless they are well known. If the letters are said one by one,
  hyphenate them: `A-P-I`, `C-L-I`, `U-R-L`, `U-T-C`. If it is said as a word, do not (`NATO`, `JSON`, `NVIDIA`).
  Say a letter-and-digit name as it is spoken: `S3` becomes `S-three`.
- Code identifiers, file paths, URLs and endpoint paths cannot be spoken clearly. Say what the thing is or does, and use
  the spoken form of any name that has to be said (`admin_scheduled` becomes "admin scheduled"). Exact strings belong in
  written notes, not in the spoken text.
- Give a phonetic spelling in parentheses, right after the first use, for an unusual or hard-to-pronounce word or name.
- Avoid direct quotations where possible. When one is needed, the attribution comes before it, not after.
- Emphasis: the sources underline. Plain text has no underline, so this guide uses `*asterisks*`.
- Never split a word or a hyphenated phrase across lines.

## House choices

The sources vary on these points. This guide fixes one choice so every script looks the same.

| Point | Sources vary | This guide |
|---|---|---|
| Speaker name | On its own line, or on the same line as the dialogue | Same line: `NAME: line` |
| Sound and music cues | A prefix word such as `SOUND:` or `GRAMS:`, or square brackets alone | Square brackets on their own line with `SFX:` or `MUSIC:` |
| Delivery directions | Parentheses or square brackets | Parentheses. Square brackets are kept for cues |
| Cue emphasis | Underlined and capitalized | Capitalized only (plain text has no underline) |
| Segments | Radio drama has scenes. A single-host broadcast has none | One scene heading for the studio, then numbered segments. This is an adaptation |
| End of script | Not sourced | `END` after the last transition |
| Header | The BBC-style format lists programme, duration, studio, writer and date | Include only the fields whose values are known. Never invent one |

## Conformance checklist

An agent checking a script confirms each item.

1. The file starts with a header block, then a scene heading, then numbered segment headings.
2. Every speech line starts with a speaker name in caps and a colon.
3. Every cue is in square brackets on its own line, in caps, and starts with `SFX:` or `MUSIC:`.
4. No cue or fade instruction sits inside a spoken line.
5. Every delivery direction is in caps, in parentheses, directly after the speaker name.
6. A speech resumed after a cue starts with `NAME: (CONT'D)`.
7. Music cues have a function and use the standard terms and fade terms where they fit.
8. No spoken line contains a symbol, an unspoken abbreviation, a code identifier, a path or a URL.
9. Numbers follow the rule above.
10. No sentence is longer than 20 words, and there are no semicolons.
11. The script's facts are unchanged. Reformatting and respelling never add, remove or alter a claim.

## Sources and confidence

The pages below were fetched through a summarizing tool, so the wording of each rule was relayed, not read verbatim.
Re-read the page before quoting it.

Read through a fetch (summarized):

- Final Draft, How to Format a Podcast Script: https://www.finaldraft.com/blog/how-to-format-podcast-script
  (speaker names in caps, sound cues in brackets on their own line, the BBC music-cue rule, parentheticals, FADE OUT)
- Weird World Studios, Basic Script Conventions in Audio Drama: https://weirdworldstudios.com/script-conventions/
  (`NAME:` speaker format, capitalized cues, `(BEAT)`, capitalized directions at the start of a line)
- EpicScribe, BBC Radio Drama Format Guide: https://epicscribe.io/blog/bbc-radio-drama-format-guide.html
  (header fields, scene headings, `(CONT'D)`, fade terms, music terms, pause notation). This is a third-party guide, not
  a BBC page. BBC Writers Room's own house-style pages ("Cue Style" and "Scene Style") were not opened.
- ZenMic, How to Write a Fiction Podcast Script: https://zenmic.com/guide/how-to-write-a-fiction-podcast-script/
  (bracketed `[SOUND: ...]` cues on separate lines; its speaker-on-own-line layout is one of the variants)
- University of Florida IFAS, News Writing for Television and Radio: https://ask.ifas.ufl.edu/wc193
  (numbers, symbols, abbreviations, semicolons, dashes, emphasis, quotations, phonetic spelling, 20-word sentences,
  contractions)

Search-result summaries only, page not opened:

- Hyphenating letter-by-letter acronyms and leaving word-like acronyms unhyphenated (F-B-I, but NATO). It appeared in
  summaries of broadcast style guides; the original page was not identified.
