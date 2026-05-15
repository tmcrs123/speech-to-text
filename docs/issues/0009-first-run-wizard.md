# 0009 — First-run wizard flow

**Type:** AFK
**Source:** `docs/PRD.md` (user story 28)

## What to build

A guided first-run flow that drives the **SettingsWindow** through a sequence of steps when **ConfigStore** has no valid configuration:

1. Welcome / one-paragraph explanation of what the tool does.
2. Choose **Transcription Backend** (`Cloud` or `Local`).
3. If `Cloud`: prompt for Groq API key, validate it with a minimal test call (e.g. a tiny dummy transcription or list-models request), persist via **ConfigStore**.
4. If `Local`: prompt for model size; download the chosen model with visible progress (the download mechanism is supplied by slice #0010); persist the model choice.
5. Confirm or remap the **Hotkey** (default `Ctrl+Shift+Space`).
6. Choose the input device (default `(System default)`).
7. Confirm summary, finish.

After completion the wizard closes; the app is fully usable. If the user closes the wizard mid-flow, the app remains unconfigured and the wizard re-opens on next launch.

## Acceptance criteria

- [ ] On first launch (no config or invalid config) the wizard appears automatically.
- [ ] Each step persists to **ConfigStore** on advance, so closing mid-wizard preserves partial progress.
- [ ] `Cloud` branch validates the API key with a minimal live call before advancing; a bad key shows a clear error and blocks the step.
- [ ] `Local` branch shows download progress and cannot advance until the model file is verified.
- [ ] Closing the window before completion leaves the app unconfigured; relaunch reopens the wizard.
- [ ] After successful completion, a hotkey tap performs an end-to-end **Dictation** with no further setup.

## Blocked by

#0008
