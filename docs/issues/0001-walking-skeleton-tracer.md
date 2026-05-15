# 0001 — Walking-skeleton tracer: hotkey → audio → cloud transcription → raw paste

**Type:** AFK
**Source:** `docs/PRD.md`

## What to build

A walking-skeleton end-to-end path that proves the whole pipeline before any module is properly extracted: tap a hard-coded `Ctrl+Shift+Space` chord, capture microphone audio at 16 kHz mono PCM, send it to Groq `whisper-large-v3-turbo` in batch mode (API key from `GROQ_API_KEY` environment variable), and paste the returned raw text into the focused window via a basic Ctrl+V (no clipboard save/restore yet).

The app runs as a minimal hidden process — no tray icon, no settings UI, no first-run wizard. Use C# / .NET 8, NAudio (or equivalent WASAPI wrapper), `HttpClient` for Groq, `WH_KEYBOARD_LL` low-level keyboard hook for the chord (per ADR-0002).

This is the **tracer bullet**. Subsequent slices replace each shortcut (env-var key, raw paste, no post-processor, hard-coded chord) with the real implementation.

## Acceptance criteria

- [ ] Tapping `Ctrl+Shift+Space` once transitions Idle → **Recording** and starts capturing 16 kHz mono PCM from the system default microphone.
- [ ] Tapping `Ctrl+Shift+Space` again stops capture and sends the captured audio to Groq `whisper-large-v3-turbo`.
- [ ] The returned text is pasted (Ctrl+V via `SendInput`) into whatever window had focus at the moment of the second tap.
- [ ] If `GROQ_API_KEY` is not set at startup, the app exits with a clear error message.
- [ ] No audio file or transcript is written to disk at any point in the lifecycle.

## Blocked by

None — can start immediately.
