# Toggle Hotkey with Esc-abort, max-duration cutoff, and queue concurrency

## Context

The user's dictation flow is "tap a chord, speak hands-free, tap again, text appears." They explicitly rejected push-to-talk because they do not want to keep a finger on the keyboard while speaking. The toggle model creates three problems that don't exist in push-to-talk:

1. **No natural way to abort a botched Recording.** Releasing the key isn't an option — there is no held key.
2. **Forgotten stop press leaves the mic open indefinitely.** A walk away from the desk could record a meeting, phone call, etc.
3. **A second tap during a previous Dictation's Transcribing/Pasting phase is ambiguous.** Is it "cancel the in-flight one and start over" or "start a new one, keep the old one"?

## Decision

- **Hotkey** is a chord (default `Ctrl+Shift+Space`, user-remappable) that toggles **Recording** on tap. Bound via a low-level keyboard hook (`WH_KEYBOARD_LL`) so it can detect a clean tap.
- **`Esc`** pressed during **Recording** aborts the current **Dictation**: audio is discarded, nothing is sent to the **Transcription Backend**, nothing is pasted.
- **Recording** auto-stops at a configurable max-duration cutoff (default 120s). The captured audio so far transcribes normally — this is a safety net, not an abort.
- **Hotkey tap during another Dictation's `Transcribing` or `Pasting` phase** queues a new **Dictation** behind the in-flight one. Nothing in flight is cancelled. The target window for each **Dictation** is captured at the moment its **Recording** ends, so queued dictations paste into the correct window even if focus has moved by the time they're ready.

## Considered alternatives

- **Push-to-talk (hold to record).** Naturally solves all three problems (release = stop, no max needed, no concurrency ambiguity) but rejected by the user on ergonomics — they don't want to hold keys.
- **"Cancel previous" on Hotkey tap during Transcribing.** Rejected via grilling: it loses already-spoken content if the user starts a new Dictation in the ~300ms window before transcription returns.
- **Silence-based auto-stop.** Considered for the max-duration role, rejected as default-on because pausing to think mid-sentence would falsely terminate Recording. Could revisit as an opt-in.
- **`SetForegroundWindow` at paste-time instead of Recording-end-captured HWND.** Simpler implementation but causes queued dictations to paste into whichever window currently has focus, which is surprising and dangerous.

## Consequences

- The keyboard hook receives every system keypress; it must be careful not to leak or steal keys outside of `Hotkey` and `Esc` while Recording.
- Pasting into a queued **Dictation**'s captured HWND requires `SetForegroundWindow` (or the `AttachThreadInput` workaround) before dispatching Ctrl+V, since the user may have moved focus.
- The state machine has four discrete states (Idle, Recording, Transcribing, Pasting) and the **Hotkey**'s effect depends on which state is active — this asymmetry must be implemented and tested explicitly.
