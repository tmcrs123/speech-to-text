# 0004 — WindowTargeter + ClipboardPaster (save/restore + focus restoration)

**Type:** AFK
**Source:** `docs/PRD.md` (user stories 7, 8, 29)

## What to build

Two new modules that replace the raw paste from slice #0001:

1. **WindowTargeter**
   - `CaptureHwndNow()` returns the current foreground HWND.
   - `RestoreFocus(hwnd)` brings that window back to the foreground, using the `SetForegroundWindow` + `AttachThreadInput` workaround to handle Win32 foreground-window restrictions. This must be implemented from the start — `SetForegroundWindow` alone occasionally returns `true` without actually changing focus.

2. **ClipboardPaster**
   - `Paste(string text)` saves the current clipboard contents (text plus common formats where feasible), writes `text` to the clipboard, dispatches Ctrl+V via `SendInput`, and restores the prior clipboard contents ~100 ms after the paste.
   - Handles unicode and emoji correctly.

Wire them into the pipeline: capture the HWND at the moment **Recording** ends (second hotkey tap or max-duration cutoff); after transcription + post-processing, restore focus to that HWND before calling **ClipboardPaster**.

## Acceptance criteria

- [ ] Target HWND is captured at the moment **Recording** ends — not at hotkey-press time, not at paste time.
- [ ] After transcription, focus is restored to the captured HWND before paste, even if the user has switched focus during **Transcribing**.
- [ ] Prior clipboard contents are restored within ~100 ms of the paste.
- [ ] Unicode and emoji paste correctly into Notepad, a browser address bar, and a terminal window.
- [ ] Paste works reliably in apps that previously failed under raw paste (terminals, IDEs).

## Blocked by

#0001
