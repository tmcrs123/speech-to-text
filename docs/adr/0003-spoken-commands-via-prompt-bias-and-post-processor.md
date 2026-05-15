# Spoken Commands via Whisper prompt-bias + Post-Processor

**Status: Superseded by [ADR-0004](0004-drop-post-processor-and-spoken-commands.md) (2026-05-15).**

The decision below was never validated end-to-end. In practice the `initial_prompt` biasing was inconsistent and the `\n`-stripping rule swallowed line breaks the user wanted. The Post-Processor and the Spoken Command vocabulary have been removed; Groq's transcription output is now pasted verbatim. The original record is retained below for history.

## Context

The user wants Whisper's automatic punctuation as the default, *plus* the ability to say keywords ("comma", "full stop", "question mark", etc.) to force punctuation in places Whisper wouldn't infer it. Critically, line breaks (`\n`, `\n\n`) must **never** be auto-inferred — they may only appear when the user explicitly says "new line" or "new paragraph".

Whisper's default behaviour is non-deterministic when the speaker dictates a command keyword: sometimes Whisper outputs the literal word (`...world comma the dog...`), sometimes it auto-substitutes the punctuation and eats the word (`...world, the dog...`), and sometimes both (`...world, comma, the dog...`). A post-processor that only looks at the output text can't reliably distinguish these cases.

## Decision

Two-part design:

1. **Bias Whisper via `initial_prompt`.** Every transcription request — Cloud or Local — is invoked with an `initial_prompt` that lists every **Spoken Command** as a literal word, e.g.: *"The speaker dictates punctuation commands such as: comma, full stop, question mark, exclamation mark, colon, semicolon, new line, new paragraph, open quote, close quote, open paren, close paren."* This biases the decoder to emit the keywords as literal words rather than substituting punctuation.

2. **Run a synchronous `Post-Processor`** between **Transcribing** and **Pasting**. It:
   - Substitutes each **Spoken Command** match (case-insensitive) with the canonical output (`comma` → `, `, `full stop` → `. `, `open quote` → `"`, `new line` → `\n`, etc.).
   - Strips any `\n` that Whisper emitted on its own (line breaks must come from explicit commands only).
   - Capitalizes the first letter after any inserted `.`, `?`, `!`, `\n`, or `\n\n`.
   - Accepts that single-word keywords (`comma`, `colon`, `semicolon`) will occasionally false-positive when the user dictates those words literally. The frequency is low enough to prefer easy editing over more complex disambiguation.

v1 keyword set: `comma`, `full stop`, `question mark`, `exclamation mark` (alias `exclamation point`), `colon`, `semicolon`, `open quote`, `close quote`, `open paren`, `close paren`, `new line`, `new paragraph` (alias `paragraph`).

## Considered alternatives

- **No prompt; post-processor handles both keyword-present and keyword-already-substituted output.** Rejected — Whisper's non-determinism means the post-processor would have to recognise both `...world comma the dog...` and `...world, the dog...` as equivalent and treat them the same. More complex code, more edge cases, and still occasionally wrong.
- **Disable Whisper's auto-punctuation entirely, rely 100% on spoken commands.** Rejected — the user explicitly wants auto-punctuation as the baseline. Reading aloud every comma is exhausting.
- **Require multi-word phrases for every command (`insert comma`, `insert colon`).** Rejected — symmetrical and false-positive-free, but `insert comma` is two extra words for the most-frequent keyword. The user accepted the false-positive trade-off for ergonomics.
- **Context-sensitive heuristic (treat 'comma' as command only at sentence boundaries).** Rejected — fragile and unpredictable. Easier to occasionally edit than to debug heuristic misses.

## Consequences

- Both **Cloud Backend** and **Local Backend** must accept and forward an `initial_prompt`; the abstraction defined in ADR-0001 widens slightly to include it.
- The `Post-Processor` becomes a stable, testable unit — its input is predictable because of the prompt-bias, so unit tests can pin down exact input/output pairs.
- The keyword list is configuration, not code — adding `em dash` / `dash` / `hyphen` later is a list edit plus a prompt-string edit. No structural change.
- Whisper's `initial_prompt` is bounded (~244 tokens for OpenAI / Groq Whisper). The v1 keyword list is well under that.
