# Transcription Status Popup (front-of-queue) and copy-to-clipboard output mode

**Status: Accepted (2026-07-02).**

## Context

Feedback about a **Dictation** was previously peripheral: the tray icon swaps colour per phase and start/stop pings play. After **Recording** ends there was no direct, readable confirmation that **Transcribing** was underway or had completed. Separately, the only delivery on success was **Pasting** — copy to clipboard, dispatch Ctrl+V into the target window, then restore the prior clipboard — so the transcript never remained on the clipboard.

Two requests followed: (1) an on-screen popup that says "Transcribing…" then "Transcription finished", and (2) an option to leave the transcript on the clipboard for manual pasting instead of auto-pasting.

ADR-0005 established that any floating overlay must be passive and click-through, and its Consequences section explicitly asked that a *future third surface* choose its queue semantics (front-of-queue vs any-Recording) deliberately, per that surface's purpose.

## Decision

**The Status Popup is a third passive surface, and it uses front-of-queue semantics.** It is a click-through, always-on-top, never-focused WPF window (`WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, exactly like the **Recording Indicator**) at the bottom-centre of the focused monitor. It is driven by `DictationOrchestrator.StateChanged` (front-of-queue), not `RecordingActiveChanged` — because its job is to report *the transcription that is completing*, which is inherently the front-of-queue **Dictation**, the same one the tray icon tracks. It shows "Transcribing…" on entering **Transcribing** and "Transcription finished" (auto-dismissed after ~2 s) when the front-of-queue **Dictation** reaches Idle *from* **Pasting**. The success path always passes through **Pasting**, so a failed/empty **Dictation** (which leaves **Transcribing** straight to Idle) never shows "finished".

**The output mode is a per-machine setting owned by the paster, not the orchestrator.** A new config value `OutputMode` (`"paste"` | `"clipboard"`, default `"paste"`) selects delivery. `ClipboardPasterAdapter` reads it live at paste time via an injected `Func<bool>`; when copy-only, it calls `ClipboardPaster.Copy` (set clipboard as UnicodeText, no Ctrl+V, no save/restore) instead of `ClipboardPaster.Paste`. `DictationOrchestrator` is untouched — it still transitions through `Phase.Pasting` regardless of mode, which keeps the state machine (and therefore the Status Popup's "finished" signal) identical in both modes.

## Considered alternatives

- **Drive the popup from `RecordingActiveChanged` / any-Recording semantics (as the Recording Indicator does).** Rejected. The popup reports transcription progress of a specific **Dictation**; any-Recording is about the mic being open, a different question already answered by the Recording Indicator.

- **Make the popup interactive — e.g. a "Copy" button at finish time.** Rejected for the same reason ADR-0005 rejected an interactive indicator: a focus-stealing overlay risks breaking `WindowTargeter.CaptureHwndNow()` / `RestoreFocus` and mis-routing the paste. Output mode is therefore a Settings choice, not an in-popup action.

- **Teach `DictationOrchestrator` about output mode (e.g. a `Func<bool>` constructor arg driving `StartPaste`).** Rejected. It would change the widely-tested orchestrator constructor and leak a delivery concern into the state machine. Keeping the decision in the paster is a smaller seam and leaves `Phase.Pasting` intact.

## Consequences

- A new surface, `StatusPopupWindow`, subscribes to `StateChanged` and is created once at startup (like the Recording Indicator) to avoid first-show latency. Visibility is gated on a new `ShowStatusPopup` config flag (default true), mirroring `ShowRecordingIndicator`.
- `OutputMode` and `ShowStatusPopup` are read live and excluded from the Settings "changed → restart" check, so both take effect without an app restart.
- In copy-only mode `StartPaste` still calls `RestoreFocus` before delivery; with no Ctrl+V this merely re-foregrounds the target window and is harmless. If that ever becomes undesirable, the fix belongs in the paster/adapter, not the orchestrator.
- The tray icon, Recording Indicator, and Status Popup are now three surfaces with deliberately-chosen semantics: front-of-queue (tray, popup) vs any-Recording (indicator). Future surfaces should keep making this choice explicitly.
