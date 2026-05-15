# PRD — Speech-to-Text (v1)

## Problem Statement

I dictate frequently on Windows but the built-in Windows speech-to-text experience is unreliable and slow. I want a focused tool that lets me press a chord, speak hands-free, press the chord again, and have the text appear in whatever app I had focused — fast and accurate. The same tool needs to work on a laptop (no GPU, online) and a workstation (powerful GPU, offline-capable) without forcing me to compromise on either machine.

## Solution

A Windows tray utility (C# / .NET 8 / WPF) that listens for a global toggle **Hotkey**, captures microphone audio into a **Dictation**, transcribes it through a pluggable **Transcription Backend** (Groq Whisper API on laptops, local Whisper.NET on GPU machines), runs the result through a **Post-Processor** that turns **Spoken Commands** like "comma", "full stop", "new paragraph" into the corresponding punctuation/line breaks, and pastes the final text into the window that was focused when **Recording** ended. The full state machine is captured in `CONTEXT.md` and the architectural decisions in `docs/adr/0001..0003`.

## User Stories

1. As a user on my workstation, I want to tap a keyboard chord, speak hands-free, and tap the chord again, so that the text appears in whatever app I had focused without holding a key down.
2. As a user, I want the default **Hotkey** to be `Ctrl+Shift+Space`, so that it doesn't collide with any common app.
3. As a user, I want to remap the **Hotkey** from the settings window, so that I can avoid conflicts with software that uses my chosen default.
4. As a user, I want **Recording** to auto-stop after 120 seconds (configurable), so that walking away from my desk doesn't leave the microphone open indefinitely.
5. As a user, I want pressing `Esc` during **Recording** to abort the **Dictation** entirely — no transcription, no paste — so that I can cleanly cancel when I realise I'm about to say the wrong thing.
6. As a user, I want a tap of the **Hotkey** during another **Dictation**'s **Transcribing** or **Pasting** phase to start a new **Dictation** that is queued behind the in-flight one, so that I can fire off rapid consecutive dictations without losing any.
7. As a user, I want each **Dictation**'s target window to be locked in at the moment **Recording** ends, so that queued dictations paste into the correct app even if I've switched focus while waiting.
8. As a user, I want the transcribed text to appear via clipboard-paste (Ctrl+V), with my prior clipboard contents restored ~100ms after the paste, so that the tool works in every app that supports paste without permanently hijacking my clipboard.
9. As a user, I want a **Cloud Backend** option that calls Groq `whisper-large-v3-turbo` in batch mode, so that I get fast, accurate transcription on machines without GPUs.
10. As a user, I want my Groq API key stored via Windows DPAPI (CurrentUser scope), so that other users on the same machine can't read it from disk.
11. As a user, I want a **Local Backend** option using Whisper.NET, so that I can dictate offline and keep sensitive content on my machine when I have the GPU to support it.
12. As a user, I want to pick the local Whisper model size (small / medium / large-v3-turbo) at first-run wizard time per machine, so that I match the model to that machine's hardware.
13. As a user, I want each machine to use exactly one **Transcription Backend** picked at first-run, so that I'm not juggling runtime toggles on machines that have a clear right answer.
14. As a user, I want the tray icon to reflect the current phase of any in-flight **Dictation** (Idle / Recording / Transcribing / Error-flash), so that I can glance at it to confirm what's happening.
15. As a user, I want a subtle ping sound when **Recording** starts and a lower pong when it ends, so that I can confirm the **Hotkey** registered without looking at the tray.
16. As a user, I want the start/stop sounds to be mutable from settings, so that I can silence them in shared workspaces.
17. As a user, I want a **Dictation** that produces empty or whitespace-only text, or that fails in **Transcribing**, to be silently dropped with a brief red tray flash, so that I don't end up with garbage pasted into my editor.
18. As a user, I want Whisper to auto-infer most sentence-level punctuation (periods, commas, question marks) from prosody, so that I don't have to verbalise every comma.
19. As a user, I want to be able to say **Spoken Commands** — `comma`, `full stop`, `question mark`, `exclamation mark`, `colon`, `semicolon`, `open quote`, `close quote`, `open paren`, `close paren` — to force punctuation where I want it, so that I don't have to manually edit after.
20. As a user, I want `new line` and `new paragraph` (alias `paragraph`) to be the **only** way line breaks ever appear in my output, so that Whisper never inserts unexpected breaks.
21. As a user, I want the **Post-Processor** to capitalize the first letter after any inserted `.`, `?`, `!`, `\n`, or `\n\n`, so that the output reads naturally without manual fixing.
22. As a user, I want Whisper to be biased via `initial_prompt` to treat **Spoken Commands** as literal words, so that the **Post-Processor** has predictable input and reliably converts them.
23. As a user, I accept that single-word commands like `comma`, `colon`, `semicolon` will occasionally trigger when I dictate those words literally, so that I get the ergonomic short keyword instead of an `insert comma`-style multi-word form.
24. As a user, I want the app to auto-start on Windows login (toggleable in settings), so that it's always ready without me launching it manually.
25. As a user, I want the app to use the system default microphone by default but let me pick a specific capture device in settings, so that swapping headsets works automatically while I retain explicit control.
26. As a user, I want no audio or transcripts kept on disk, so that my dictations don't accumulate as a privacy liability.
27. As a user, I want a WPF settings window opened from the tray's right-click menu with tabs for Backend, Hotkey, Audio, and About, so that I can reconfigure without editing files.
28. As a user, I want the first-run wizard to walk me through picking backend, entering API key (cloud) or downloading model (local), confirming the **Hotkey**, and picking a microphone, so that I'm set up in one guided flow.
29. As a user, I want the focused window to be restored before paste (via `SetForegroundWindow` / `AttachThreadInput`), so that text reliably lands in the captured target even when focus has moved.
30. As a user, I want any `\n` Whisper might emit on its own to be stripped by the **Post-Processor**, so that line breaks are exclusively driven by `new line` / `new paragraph` commands.

## Implementation Decisions

### Stack

- **C# / .NET 8** with **WPF** for the settings window and tray icon hosting.
- **NAudio** (or equivalent WASAPI wrapper) for audio capture at 16kHz mono 16-bit PCM (Whisper's expected input).
- **Whisper.NET** NuGet for the **Local Backend** (whisper.cpp under the hood; CUDA / Vulkan / CPU runtimes).
- Standard .NET `HttpClient` for the **Cloud Backend**'s Groq API calls.
- Windows DPAPI via `System.Security.Cryptography.ProtectedData` (CurrentUser) for the encrypted API key.
- Low-level keyboard hook (`WH_KEYBOARD_LL`) via P/Invoke for the **Hotkey** and `Esc` detection — `RegisterHotKey` is insufficient because we may need to support bare-modifier remaps in future and we need `Esc` to be observable only during **Recording**.
- Configuration persisted as a plain TOML or JSON file under `%APPDATA%\SpeechToText\config.{ext}` (path TBD; not load-bearing).

### Modules

The implementation is partitioned into the following modules. Deep modules have simple stable interfaces and hide most of the complexity:

- **PostProcessor** (deep, pure). Input: raw text from a **Transcription Backend** plus the keyword-mapping config. Output: final text ready to paste. Responsible for **Spoken Command** substitution (curated keyword set, case-insensitive), capitalization after inserted sentence enders and line breaks, and stripping of any Whisper-emitted `\n`. No I/O, no state.
- **DictationOrchestrator** (deep). Owns the state machine (Idle → Recording → Transcribing → Pasting → Idle) and the queue. Subscribes to `HotkeyListener` events. Owns the **Recording** max-duration timer (default 120s). Handles `Esc`-abort during **Recording**. On **Recording** end, captures the target HWND, hands audio + `initial_prompt` to the configured `TranscriptionBackend`, runs the response through `PostProcessor`, then dispatches to `ClipboardPaster` after restoring focus via `WindowTargeter`. Drops empty/failed dictations silently and signals the tray to flash error.
- **TranscriptionBackend** (interface). Single method: `Task<string> TranscribeAsync(byte[] pcmAudio, string initialPrompt, CancellationToken ct)`. Two implementations:
  - **CloudBackend** — Groq `whisper-large-v3-turbo`, batch mode, JSON response, forwards `initial_prompt`.
  - **LocalBackend** — Whisper.NET with per-machine model size, forwards `initial_prompt`.
- **ConfigStore** (deep). Per-machine config persistence. DPAPI-encrypts the Groq API key on write, decrypts on read. Exposes typed accessors per setting (no raw JSON/TOML leaks to callers). Tracks schema version for future migration.
- **HotkeyListener** (shallow, OS-bound). Installs `WH_KEYBOARD_LL`. Emits `HotkeyTapped` events when the configured chord is tapped cleanly (down-up with no other intervening keys) and `EscPressed` events only while `DictationOrchestrator` is in **Recording**. Does not consume keys not relevant to the app.
- **AudioCapturer** (medium, OS-bound). Enumerates capture devices, exposes "use system default" mode, captures 16kHz mono PCM into a memory buffer. Start / Stop / GetCapturedAudio interface. Discards buffer on Stop after handing it off.
- **WindowTargeter** (shallow, OS-bound). Capture-HWND-now / RestoreFocus(HWND). Encapsulates the `SetForegroundWindow` / `AttachThreadInput` dance.
- **ClipboardPaster** (shallow, OS-bound). `Paste(string text)`: save current clipboard contents, write text, send Ctrl+V via `SendInput`, restore prior contents after a short delay. Handles unicode + emoji.
- **TrayIcon** (shallow, OS-bound). Owns the system tray icon, switches its image per `DictationOrchestrator` phase, plays start/stop pings via `SoundPlayer`, exposes right-click menu (Settings, Quit).
- **SettingsWindow** (shallow, UI). WPF window with Backend / Hotkey / Audio / About tabs. Also drives the first-run wizard flow against `ConfigStore`.

### Pipeline

For each successful **Dictation**:

```
HotkeyListener.HotkeyTapped (Idle)
  → DictationOrchestrator: state = Recording
  → AudioCapturer.Start, TrayIcon: Recording, start-ping
HotkeyListener.HotkeyTapped (Recording) | max-duration timer
  → AudioCapturer.Stop, TrayIcon: Transcribing, stop-pong
  → WindowTargeter.CaptureHwndNow
  → DictationOrchestrator: state = Transcribing
  → TranscriptionBackend.TranscribeAsync(audio, initial_prompt, ct)
  → PostProcessor(raw) → final text
  → DictationOrchestrator: state = Pasting
  → WindowTargeter.RestoreFocus(capturedHwnd)
  → ClipboardPaster.Paste(final)
  → DictationOrchestrator: state = Idle (pop queue if non-empty)
```

### Spoken Commands (full v1 set)

| Spoken phrase | Substitution |
|---|---|
| `comma` | `, ` |
| `full stop` | `. ` |
| `question mark` | `? ` |
| `exclamation mark` / `exclamation point` | `! ` |
| `colon` | `: ` |
| `semicolon` | `; ` |
| `open quote` | `"` |
| `close quote` | `"` |
| `open paren` | `(` |
| `close paren` | `)` |
| `new line` | `\n` |
| `new paragraph` / `paragraph` | `\n\n` |

The `initial_prompt` sent to Whisper enumerates every phrase above to bias the decoder against auto-substitution.

### State machine

`DictationOrchestrator` exposes four states: `Idle`, `Recording`, `Transcribing`, `Pasting`. Transitions:

- `Idle` + `HotkeyTapped` → `Recording`
- `Recording` + `HotkeyTapped` → `Transcribing`
- `Recording` + `EscPressed` → `Idle` (audio discarded)
- `Recording` + `MaxDurationElapsed` → `Transcribing`
- `Transcribing` + `TranscriptionSucceeded` → `Pasting`
- `Transcribing` + `TranscriptionFailedOrEmpty` → `Idle` (tray error flash)
- `Pasting` + `PasteDone` → `Idle` (then pop next queued Dictation if any)
- `Transcribing|Pasting` + `HotkeyTapped` → queue a new **Dictation** at the tail; the in-flight one is not interrupted

### Configuration schema (per machine)

Stored in user-scoped app data:

- `transcription_backend`: `"cloud" | "local"`
- `groq_api_key_dpapi`: DPAPI-encrypted ciphertext (cloud only)
- `local_model`: `"small" | "medium" | "large-v3-turbo" | …` (local only)
- `hotkey`: serialised chord descriptor (e.g., `Ctrl+Shift+Space`)
- `max_recording_seconds`: integer, default `120`
- `start_stop_sounds_enabled`: bool, default `true`
- `auto_start_on_login`: bool, default `true`
- `input_device_id`: nullable device id (null = system default)
- `schema_version`: integer

### Decisions encoded in ADRs

- ADR-0001 — Pluggable **Transcription Backend** with per-machine selection.
- ADR-0002 — Toggle **Hotkey** with `Esc`-abort, max-duration cutoff, and queue concurrency.
- ADR-0003 — **Spoken Commands** via Whisper `initial_prompt` bias + **Post-Processor**.

## Testing Decisions

Good tests in this codebase exercise **external behaviour** — the observable inputs and outputs of a module — without poking at internal state. They should survive an implementation refactor of the module under test.

Three modules get unit tests in v1:

### PostProcessor

Tested as a pure function. Each test is a triple of (raw input string, expected output string, optional keyword-config override). Coverage targets:

- Every keyword in the v1 **Spoken Command** set produces the correct substitution in the middle of a sentence, at sentence start, at sentence end.
- Single-word command false-positives are accepted: `i added a comma here` becomes `I added a, here` (documented behaviour, not a bug).
- Capitalization after inserted `.`, `?`, `!`, `\n`, `\n\n` works regardless of the original case of the following word.
- Whisper-emitted `\n` characters are stripped from input.
- Adjacent `open paren` / `close paren` and `open quote` / `close quote` produce correct spacing (`( … )` with leading space before `(` only if mid-sentence; no internal space).
- Empty input returns empty string. Whitespace-only input returns empty string.
- Case-insensitive matching: `New Line`, `NEW LINE`, `new line` all behave identically.

### DictationOrchestrator

Tested with fakes / stubs for `HotkeyListener`, `AudioCapturer`, `TranscriptionBackend`, `PostProcessor` (or real, since pure), `WindowTargeter`, `ClipboardPaster`. The test fixture exposes virtual time so the max-duration timer can be advanced deterministically. Coverage targets:

- Happy path: hotkey-tap → state `Recording` → hotkey-tap → state `Transcribing` → fake backend returns text → state `Pasting` → fake paster records the call → state `Idle`.
- `Esc` during **Recording** transitions to `Idle` with no call to `TranscriptionBackend` and no call to `ClipboardPaster`.
- `Esc` outside of **Recording** is ignored.
- Max-duration cutoff fires the same path as a second hotkey tap (audio still transcribed and pasted).
- Hotkey tap during `Transcribing` enqueues a new **Dictation**; the in-flight one completes and pastes; then the queued one runs.
- Hotkey tap during `Pasting` enqueues similarly.
- Target HWND is captured at the moment of **Recording** end, not at hotkey-press time or paste time. Verified by checking the HWND passed to `WindowTargeter.RestoreFocus`.
- Empty transcription result and failed transcription both cause silent drop (no paste call) and emit an error signal to the tray fake.
- Cancellation token from `Esc` propagates: if `Esc` could occur during `Transcribing` (it cannot per the design — `Esc` is only honoured during **Recording** — assert this explicitly).

### ConfigStore

Tested against a temp directory plus real DPAPI (CurrentUser DPAPI works in test contexts since tests run as the developer). Coverage targets:

- Round-trip of every config setting through write → read.
- API key round-trip: write plaintext → file on disk does not contain the plaintext → read returns plaintext.
- Missing config file on first read: returns defaults, does not throw.
- Corrupt/unreadable config file: returns defaults, logs a warning (no throw to callers).
- Schema-version handling: future-versioned file is tolerated (or fails clearly — pick one and assert it).
- File concurrency: two near-simultaneous writes do not corrupt the file (acceptable to serialize internally).

### Prior art

This is a greenfield repo (no existing tests). The test project structure should follow standard xUnit conventions in a parallel `.Tests` project per implementation project. No pre-existing patterns to mirror.

### Out of test scope

The OS-bound shallow modules (`HotkeyListener`, `AudioCapturer`, `WindowTargeter`, `ClipboardPaster`, `TrayIcon`, `SettingsWindow`) are covered by manual smoke testing in v1. Automated coverage for these would require an OS automation framework and is deferred.

## Out of Scope

- **Push-to-talk mode.** Explicitly rejected in ADR-0002.
- **Streaming transcription.** Batch-only via Groq / Whisper.NET. Adding Deepgram-style streaming is a future option.
- **Voice activity detection / silence-based auto-stop.** Considered and rejected as default-on. Could be added later as opt-in.
- **Spoken commands beyond punctuation and line breaks** (e.g., `delete that`, `select all`, `capital`, `all caps`). Out of v1.
- **Quote/paren disambiguation niceties** — smart-quote pairs, nested parens. v1 inserts straight ASCII characters.
- **Dash / hyphen / em-dash spoken commands.** Considered, rejected as ambiguous for v1.
- **Multi-language support / language hint.** Whisper auto-detects; no UI to pin a language. Out of v1.
- **Custom vocabulary in `initial_prompt`** (project names, jargon). Considered, deferred. The plumbing supports it once the UI is added.
- **Audio + transcript history on disk.** Default-off concept was considered and dropped from v1 entirely. No history surface in the settings window.
- **Updater / telemetry / crash reporting.** Out of v1.
- **Multiple **Hotkey**s** (e.g., different chords for different backends or modes). v1 has exactly one configured **Hotkey**.
- **Switching **Transcription Backend** at runtime.** Per-machine config only; restart the app to change.
- **Linux / macOS support.** Windows-only.

## Further Notes

- The repo currently has no GitHub remote and no issue tracker configured. This PRD is published as `docs/PRD.md`; when an issue tracker is set up (`/setup-matt-pocock-skills`), it should be migrated there and labelled `ready-for-agent`.
- The design captured in `CONTEXT.md` and ADRs 0001–0003 should be treated as the source of truth for terminology and architectural rationale. Any new term that emerges during implementation should be added to `CONTEXT.md` immediately, not batched.
- `Win32` foreground-window restrictions can occasionally cause `SetForegroundWindow` to fail silently (it returns true but the foreground doesn't change). The `AttachThreadInput` workaround should be implemented from the start to avoid intermittent paste-into-wrong-window bugs.
- Groq's Whisper API has a ~244-token `initial_prompt` limit. The v1 keyword list fits comfortably, but if user-supplied vocabulary is added later (deferred), prompt-length validation will be needed.
- Whisper.NET supports CUDA, Vulkan, OpenVINO, CoreML, and CPU runtimes. The first-run wizard should detect the available runtime and surface only viable model sizes for that runtime; details deferred to implementation.
