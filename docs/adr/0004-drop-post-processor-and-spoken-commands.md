# Drop the Post-Processor and Spoken Commands; paste Groq output verbatim

**Status: Accepted (2026-05-15). Supersedes [ADR-0003](0003-spoken-commands-via-prompt-bias-and-post-processor.md).**

## Context

ADR-0003 introduced a two-part design to let the user say "comma", "full stop", "new paragraph", etc. and have those words substituted by a synchronous Post-Processor that ran between **Transcribing** and **Pasting**. To make the substitution reliable, Groq's Whisper call was given an `initial_prompt` that listed every keyword as a literal word, biasing the decoder to emit them verbatim instead of auto-substituting punctuation.

In practice this did not work as expected:

- The `initial_prompt` biasing was inconsistent. Whisper sometimes emitted the literal keyword, sometimes silently substituted the punctuation, and sometimes did both for the same utterance — exactly the non-determinism ADR-0003 was meant to eliminate. The Post-Processor could only see the text; it could not tell which branch had fired, so its substitutions compounded with Whisper's own.
- The "strip every `\n` Whisper emitted" rule was load-bearing for the design but lossy in practice. Whisper-emitted paragraph breaks that genuinely matched the speaker's intent were being eaten, leaving the user with a wall of text and no way to recover the structure short of re-dictating.
- Single-word keywords (`comma`, `colon`, `semicolon`) false-positived often enough to be annoying when the user dictated those words literally — the trade-off ADR-0003 acknowledged in writing turned out to feel worse than predicted in daily use.

The Punctuation Mode toggle floated in issue #14 was an attempt to give the user an escape hatch from these problems by switching the Post-Processor off. That ticket is no longer needed once the Post-Processor itself is gone.

## Decision

Remove the Post-Processor and the Spoken Command vocabulary entirely. The pipeline becomes:

1. **Recording** ends.
2. **Transcribing**: send the WAV to the configured **Transcription Backend** with no `initial_prompt`. Use whatever the backend returns.
3. **Pasting**: write the returned text to the clipboard verbatim — no substitution, no capitalization, no newline manipulation — and dispatch Ctrl+V.

Concretely:

- `src/PostProcessor.cs` is deleted.
- `GroqClient.TranscribeAsync` drops the `initialPrompt` parameter.
- `Program.cs` drops the `InitialPrompt` constant and the `PostProcessor.Process(raw)` call; the raw Groq output is pasted directly.
- The **Post-Processor** and **Spoken Command** terms are removed from `CONTEXT.md`. The **Pasting** definition no longer mentions a post-processing step.
- The `tests/` project (which only exercised the Post-Processor) is removed along with it. New tests will be added when there is non-trivial logic worth testing.

## Considered alternatives

- **Keep ADR-0003 but harden it.** Rejected. The non-determinism is in Whisper's decoder, not in our code — there is no realistic amount of post-processing that recovers from "Whisper sometimes substitutes the punctuation, sometimes doesn't, for the same utterance." Every fix we discussed pushed complexity into the Post-Processor without removing the underlying ambiguity.
- **Keep the Post-Processor but make it opt-in via a Punctuation Mode toggle** (the issue #14 direction). Rejected. A toggle papers over the unreliability rather than removing it, and adds a configuration surface, a settings-window control, and a first-run-wizard step for a feature whose value we have not actually established. If the demand for explicit-punctuation dictation comes back, we can reintroduce it as a separate feature on top of a working baseline.
- **Disable Whisper's auto-punctuation entirely and require explicit commands.** Rejected, same reason as in ADR-0003 — exhausting to dictate, and we no longer trust the substitution layer to be reliable either.

## Consequences

- The user no longer has a way to insert explicit punctuation or line breaks by speaking keywords. They rely entirely on Whisper's auto-punctuation, and edit the pasted text afterwards if it is wrong.
- The **Transcription Backend** abstraction (ADR-0001) narrows: it no longer needs to accept or forward an `initial_prompt`. ADR-0001 is updated implicitly — any future backend (e.g. the Local Backend in issue #10) takes a WAV and returns text, with no prompt parameter.
- Issue #3 (the PostProcessor build-out) is closed as wontfix.
- Issue #14 (Punctuation Mode in ConfigStore + Settings UI) is closed as wontfix — its entire premise was a toggle between auto and the Post-Processor, and the Post-Processor no longer exists.
- The `tests/` project is gone for now. When code accrues that genuinely benefits from tests (ConfigStore round-tripping, the DictationOrchestrator state machine), we'll reintroduce a test project at that point rather than carrying an empty harness.
- If a Spoken Commands feature is ever revisited, it should start from a fresh ADR — not by un-superseding ADR-0003 — so that the new design is evaluated on its own merits with the lessons from this attempt in mind.
