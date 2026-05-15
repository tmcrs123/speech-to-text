# 0005 — DictationOrchestrator: full state machine + queue concurrency

**Type:** AFK
**Source:** `docs/PRD.md` (user stories 6, 17, 26; ADR-0002)

## What to build

Extract the **DictationOrchestrator** module that owns the entire **Dictation** lifecycle.

States: `Idle`, `Recording`, `Transcribing`, `Pasting`.

Transitions (from PRD State Machine section):

- `Idle` + `HotkeyTapped` → `Recording`
- `Recording` + `HotkeyTapped` → `Transcribing`
- `Recording` + `EscPressed` → `Idle` (audio discarded)
- `Recording` + `MaxDurationElapsed` → `Transcribing`
- `Transcribing` + `TranscriptionSucceeded` → `Pasting`
- `Transcribing` + `TranscriptionFailedOrEmpty` → `Idle` + emit error-flash event
- `Pasting` + `PasteDone` → `Idle` (pop next queued Dictation if any)
- `Transcribing | Pasting` + `HotkeyTapped` → enqueue a new **Dictation** at the tail of the FIFO queue; the in-flight Dictation is **not** interrupted.

A **Dictation** whose transcription result is empty / whitespace-only or whose transcription failed (network error, API error) is **silently dropped** — no clipboard touch, no paste call — and an error-flash event is emitted for the tray to consume (slice #0006).

Inject **HotkeyListener**, **AudioCapturer**, **TranscriptionBackend**, **WindowTargeter**, **ClipboardPaster**, **PostProcessor**, and a clock abstraction so the orchestrator is testable in isolation.

**Unit tests required** (per PRD Testing Decisions). Use virtual time for the max-duration timer so tests are deterministic.

Architectural assertion (user story 26): no audio or transcripts are written to disk anywhere in the orchestrator or its collaborators.

## Acceptance criteria

- [ ] All state transitions in the table above are implemented and tested.
- [ ] Happy path: `HotkeyTapped` → `Recording` → `HotkeyTapped` → `Transcribing` → backend returns text → `Pasting` → paste call made → `Idle`.
- [ ] Hotkey tap during `Transcribing` enqueues; in-flight Dictation completes and pastes; queued Dictation runs next.
- [ ] Hotkey tap during `Pasting` enqueues similarly.
- [ ] Empty transcription result → no paste, error-flash event emitted.
- [ ] Failed transcription → no paste, error-flash event emitted.
- [ ] Target HWND is captured at the moment of **Recording** end (verified via the fake **WindowTargeter**).
- [ ] `Esc` is honoured only during **Recording**; ignored in `Idle`, `Transcribing`, `Pasting`.
- [ ] Unit tests cover happy path, Esc-abort, max-duration auto-stop, queue during `Transcribing`, queue during `Pasting`, empty result, failed result, HWND capture timing.
- [ ] Verified across all tests: no audio file or transcript written to disk.

## Blocked by

#0002, #0003, #0004
