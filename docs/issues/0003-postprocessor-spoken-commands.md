# 0003 — PostProcessor with Spoken Commands + capitalization + `\n` stripping

**Type:** AFK
**Source:** `docs/PRD.md` (user stories 18–23, 30; ADR-0003)

## What to build

Implement the **PostProcessor** module per ADR-0003: a pure synchronous function that takes raw text from a **Transcription Backend** and produces the final text to paste.

Behaviour:

- Substitute every v1 **Spoken Command** with its canonical output (case-insensitive):

  | Spoken phrase | Substitution |
  |---|---|
  | `comma` | `, ` |
  | `full stop` | `. ` |
  | `question mark` | `? ` |
  | `exclamation mark` / `exclamation point` | `! ` |
  | `colon` | `: ` |
  | `semicolon` | `; ` |
  | `open quote` | `"` |
  | `close quote` | `"` |
  | `open paren` | `(` |
  | `close paren` | `)` |
  | `new line` | `\n` |
  | `new paragraph` / `paragraph` | `\n\n` |

- Capitalize the first letter after any inserted `.`, `?`, `!`, `\n`, `\n\n`.
- Strip any `\n` Whisper emitted on its own — line breaks must only originate from `new line` / `new paragraph` commands.
- Accept single-word false-positives (`comma`, `colon`, `semicolon` etc. trigger even in literal contexts) as documented behaviour.

Also widen the **Transcription Backend** interface (currently a single Groq call in slice #0001) to accept and forward an `initial_prompt` string. The app constructs a prompt that lists every **Spoken Command** as a literal word, biasing Whisper to emit them verbatim. Forward the prompt to Groq's `prompt` field.

Wire the **PostProcessor** into the pipeline between transcription and paste.

**Unit tests required** (per PRD Testing Decisions).

## Acceptance criteria

- [ ] **Transcription Backend** accepts `initial_prompt` and forwards it to Groq.
- [ ] The `initial_prompt` sent by the app enumerates every v1 **Spoken Command** as a literal word.
- [ ] "hello comma world full stop" → "Hello, world."
- [ ] "first paragraph second" → "First\n\nSecond"
- [ ] `new line`, `new paragraph`, `paragraph` are the only sources of line breaks in output; any Whisper-emitted `\n` is stripped.
- [ ] Case-insensitive matching: `Comma`, `COMMA`, `comma` substitute identically.
- [ ] Empty input → empty string; whitespace-only input → empty string.
- [ ] `open paren … close paren` produces correct spacing (`(…)` with leading space if mid-sentence, no internal space adjacent to the parens).
- [ ] Unit tests cover every keyword at start/middle/end of sentence, capitalization rules, `\n` stripping, paren/quote spacing, case-insensitivity, and empty input.

## Blocked by

#0001
