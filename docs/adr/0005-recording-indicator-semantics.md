# Recording Indicator: passive, click-through, follows any-Recording (not front-of-queue)

**Status: Accepted (2026-05-17).**

## Context

The tray icon (see `TrayIcon.cs`) gives a peripheral cue for the current **Dictation** phase. It is small, sits in the system tray, and is read with peripheral vision when the user already suspects something is happening. It reflects the **front-of-queue** phase, computed in `DictationOrchestrator.ComputeStateUnderLock()` — so when a second **Dictation** enters **Recording** at the back of the queue while the front is still **Transcribing**, the tray icon continues to show **Transcribing**. For a peripheral cue this is an acceptable compromise: the tray icon has only one slot and the user can confirm "did the second tap take?" from the start-ping sound.

The new **Recording Indicator** has a different job. Its stated purpose is *direct, unambiguous visual confirmation that a recording is underway* — visible at the bottom-centre of the focused monitor, large enough to read in foveal vision. It exists because the tray icon alone is too easy to miss; users want to glance at the place they're typing and *see* that the mic is open.

Two design questions follow from that purpose.

## Decision

**The Recording Indicator is passive and click-through.** It is rendered as an always-on-top WPF window with `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE` so that mouse events pass through to the window underneath, and it can never take keyboard focus. There is no clickable affordance to stop or abort the **Dictation** — those actions remain bound to the **Hotkey** and **Esc**.

**The Recording Indicator's visibility tracks "any Dictation in the Recording phase", not the front-of-queue state.** A new event on `DictationOrchestrator`, fired from inside the same lock that mutates the dictation queue, signals when the count of dictations in `Phase.Recording` transitions between 0 and >0. The Recording Indicator subscribes to this event. The existing `StateChanged` event (which drives the tray icon) is unchanged.

This means the Recording Indicator and the tray icon can deliberately disagree: if dictation A is **Transcribing** at the front of the queue and the user taps the **Hotkey** again, dictation B enters **Recording** at the back. The tray icon still shows **Transcribing** (front-of-queue). The Recording Indicator appears (any-Recording).

## Considered alternatives

- **Make the Recording Indicator interactive — click to stop or abort.** Rejected. The Indicator floats over arbitrary application windows. Any focus-stealing interaction risks breaking `WindowTargeter.CaptureHwndNow()`, which captures the paste target HWND at **Recording**-end and depends on focus being on the *real* target window at that instant. A mis-click that activates the Indicator window would silently send the eventual paste to the wrong place. The **Hotkey** and **Esc** already provide stop and abort affordances, both global, both keyboard-driven, both already known to the user.

- **Piggyback `StateChanged` so the Indicator and the tray icon agree.** Rejected. This would hide the Indicator during the exact moment its purpose says it should be visible — when a second mic is open behind a still-transcribing dictation. The tray icon's front-of-queue rule is acceptable because the tray icon is peripheral and supplemented by the start-ping sound; the Indicator is the opposite of peripheral, and a missing Indicator during an active mic is louder than a slightly-stale tray icon.

- **Surface the queued recording in the tray icon too** (e.g. by overlaying a count, or by reflecting any-Recording on both surfaces). Rejected. The tray icon is too small to convey a queue meaningfully, and changing its semantics now would invalidate the rule documented for it (front-of-queue phase, one phase at a time). Different surfaces, different jobs.

## Consequences

- `DictationOrchestrator` gains a new event (working name: `RecordingActiveChanged(bool)`), fired from inside the queue lock whenever the count of dictations in `Phase.Recording` transitions between 0 and >0. `StateChanged` is unchanged.
- The Recording Indicator and the tray icon will visibly disagree during queued-recording windows. This is **intended**, not a bug. New contributors are likely to flag it as one; this ADR is the canonical answer.
- The Indicator never takes focus, which is load-bearing for `WindowTargeter.CaptureHwndNow()` at **Recording**-end. Any future change that makes the Indicator interactive must come with a plan for preserving paste-target capture.
- If a third surface for **Dictation** state ever lands (e.g. a Windows toast, a Win11 widget, an OBS overlay), the choice between front-of-queue and any-Recording semantics should be made deliberately per-surface, matched to that surface's purpose — not copied blindly from either existing surface.
