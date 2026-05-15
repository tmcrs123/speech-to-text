# 0011 — Auto-start on Windows login

**Type:** AFK
**Source:** `docs/PRD.md` (user story 24)

## What to build

When `ConfigStore.AutoStartOnLogin` is `true`, ensure a `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry exists pointing at the app executable with a `--minimized` (or equivalent) argument that causes the app to start directly to the tray with no visible window.

Toggling the setting in the **About / General** area of the settings window (slice #0008) writes or removes the registry entry immediately within the same session.

Default for new installs: `true` (set by the first-run wizard).

The app's start-up path must already support starting to-tray without showing any window; verify this works regardless of whether `--minimized` was passed.

## Acceptance criteria

- [ ] On first-run completion with `auto_start_on_login = true`, the `HKCU\…\Run` entry is created with the correct executable path and argument.
- [ ] Toggling the setting off removes the entry within the same session (no restart needed).
- [ ] Toggling the setting on restores the entry.
- [ ] When launched via the Run entry, the app starts to-tray with no visible window.
- [ ] Manual launch is unaffected — also starts to-tray; the settings window opens only on explicit user action.
- [ ] Uninstalling / quitting the app does not leave a stale Run entry pointing at a missing executable (out of scope here, but document the assumption).

## Blocked by

#0007, #0008
