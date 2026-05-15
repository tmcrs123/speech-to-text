# 0008 — Settings window (WPF) with Backend / Hotkey / Audio / About tabs

**Type:** HITL (design review on layout before merge)
**Source:** `docs/PRD.md` (user stories 2, 3, 25, 27)

## What to build

A WPF settings window opened from the tray icon's "Settings…" menu item (which slice #0006 left disabled — enable it here).

Tabs:

- **Backend** — radio between `Cloud` and `Local`.
  - `Cloud`: masked text field for the Groq API key, persisted via **ConfigStore** (DPAPI-encrypted).
  - `Local`: dropdown of supported Whisper model sizes. The model-download UX itself ships in slice #0010; this tab merely records the choice.
- **Hotkey** — chord-capture control. User clicks "Set Hotkey…", presses the desired chord, the captured chord descriptor is stored. Default `Ctrl+Shift+Space`. Live re-binding: saving updates the running **HotkeyListener** — the old chord stops firing, the new one starts.
- **Audio** — dropdown of available capture devices (via WASAPI / `MMDeviceEnumerator`). First entry is `(System default)`. Re-enumerates devices on dropdown open to reflect hot-plugged headsets.
- **About** — app version, links to repo and ADRs, mute-sounds toggle, max-recording-seconds spinner.

The window is opened from the tray icon's "Settings…" menu. All changes persist via **ConfigStore** on Save (or live as you edit — design review will decide).

**HITL: design review before merge.** Before agents finalise XAML, the developer reviews proposed layouts (mockups or a first XAML pass) for:

- Tab order and grouping.
- Chord-capture UX (how the user is prompted, how Escape/cancel behaves, how invalid chords are rejected).
- Hot-plugged audio device handling.
- Masking style for the API key.
- Save-on-edit vs explicit Save button.

## Acceptance criteria

- [ ] Tray "Settings…" opens the WPF window.
- [ ] All four tabs are present and functional.
- [ ] Changes are persisted to **ConfigStore**.
- [ ] Hotkey-capture: pressing a chord and saving causes the old chord to stop firing and the new one to start, in the same session.
- [ ] Audio device dropdown reflects current devices on each open (catches hot-plugged headsets).
- [ ] API key field is masked.
- [ ] Mute-sounds toggle and max-recording-seconds value persist and take effect immediately.
- [ ] Design review sign-off recorded in PR before merge.

## Blocked by

#0007
