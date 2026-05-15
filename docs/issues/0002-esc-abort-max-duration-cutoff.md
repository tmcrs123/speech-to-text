# 0002 — Esc abort + 120s max-duration cutoff

**Type:** AFK
**Source:** `docs/PRD.md` (user stories 4, 5; ADR-0002)

## What to build

Add two stop conditions to the **Recording** phase from slice #0001:

1. **`Esc` abort.** Pressing `Esc` during **Recording** aborts the **Dictation** entirely — captured audio is discarded, nothing is sent to Groq, nothing is pasted. After abort the app returns to Idle.
2. **Max-duration cutoff.** After 120 seconds of continuous **Recording**, capture stops automatically and the audio captured so far is transcribed normally (safety net for forgotten stop-press).

`Esc` must only be honoured while **Recording**. In every other state it must pass through to the focused application unmodified — the low-level hook must not consume it.

The 120-second value is hard-coded in this slice; it becomes a config setting in slice #0007.

## Acceptance criteria

- [ ] Pressing `Esc` during **Recording** stops capture, discards the captured audio, and returns to Idle with no HTTP call and no paste.
- [ ] Pressing `Esc` outside of **Recording** is not consumed by the app — the focused window receives the keystroke normally.
- [ ] **Recording** auto-stops after 120 seconds of continuous capture.
- [ ] Audio captured before the auto-stop is transcribed and pasted normally (same path as a second `Ctrl+Shift+Space` tap).

## Blocked by

#0001
