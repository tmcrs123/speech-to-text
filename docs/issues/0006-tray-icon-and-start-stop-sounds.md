# 0006 — TrayIcon with per-phase states + subtle start/stop sounds

**Type:** AFK
**Source:** `docs/PRD.md` (user stories 14, 15, 16)

## What to build

**TrayIcon** module: a Windows system tray icon that subscribes to **DictationOrchestrator** phase events and updates its image accordingly.

Icon states:

- `Idle` → neutral mic outline
- `Recording` → solid red dot
- `Transcribing` → spinner or pulsing variant
- `Pasting` → brief transient (reuse `Transcribing` icon is acceptable)
- Error-flash event → red flash for ~2 seconds, then return to `Idle` icon

Subtle audio cues via `SoundPlayer`:

- Short higher-pitched ping at `Recording` start.
- Short lower-pitched pong at `Recording` end (any cause: hotkey tap, max-duration cutoff, `Esc` — distinguishable `Esc` tone is nice-to-have, not required).

Right-click menu on the tray icon:

- "Settings…" — disabled in this slice; wired in slice #0008.
- "Quit" — exits the app cleanly (uninstalls the keyboard hook, disposes audio, removes the tray icon).

A temporary in-memory mute toggle for sounds is acceptable in this slice; persisted setting arrives with slice #0007.

## Acceptance criteria

- [ ] Tray icon appears in the notification area on app start.
- [ ] Icon image matches the current **DictationOrchestrator** state within one UI frame of each transition.
- [ ] Error-flash plays for ~2 seconds on empty/failed Dictation, then reverts to `Idle`.
- [ ] Start-ping plays on `Recording` start; stop-pong plays when `Recording` ends.
- [ ] Right-click → Quit exits cleanly with no orphaned keyboard hook or tray icon.

## Blocked by

#0005
