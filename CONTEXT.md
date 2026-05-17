# Speech-to-Text

A Windows desktop utility that turns spoken audio into text and pastes it into the currently-focused application. The user taps a keyboard chord to start recording, speaks, taps again to stop — text appears in whatever app had focus when recording stopped.

## Language

**Dictation**:
One tap-speak-tap-paste cycle initiated by tapping the **Hotkey**. Passes through three phases in order: **Recording**, **Transcribing**, **Pasting**.
_Avoid_: session, utterance

**Hotkey**:
The keyboard chord (default Ctrl+Shift+Space) tapped to toggle a **Dictation**. First tap transitions Idle → **Recording**; second tap transitions **Recording** → **Transcribing**. User-remappable per machine.
_Avoid_: shortcut, trigger, hot-key

**Recording**:
The phase of a **Dictation** during which audio is being captured. Ends on a second **Hotkey** tap, on **Esc** (which aborts the **Dictation** entirely — no transcription, no paste), or on the max-duration cutoff (default 120s, configurable).
_Avoid_: capturing, listening

**Transcribing**:
The phase of a **Dictation** after **Recording** ends, during which captured audio is being converted to text by the **Transcription Backend**.
_Avoid_: processing, inferring

**Pasting**:
The phase of a **Dictation** after transcription returns, during which the transcribed text is written to the clipboard verbatim, Ctrl+V is dispatched to the target window, and the prior clipboard contents are restored.
_Avoid_: inserting, typing

**Recording Indicator**:
A small, always-on-top, click-through window that is visible exactly when at least one **Dictation** is in the **Recording** phase. Shows a live audio meter driven by the active mic input. Anchored to the bottom-centre of the monitor that owns the focused window at **Recording**-start. Purely passive — never takes focus, never receives input.
_Avoid_: widget, overlay, HUD, meter

**Transcription Backend**:
A pluggable component that converts captured audio into text. Concrete implementations: **Cloud Backend** (Groq) and **Local Backend** (Whisper.NET).
_Avoid_: provider, engine, model

**Cloud Backend**:
A **Transcription Backend** that sends audio to a hosted API. Currently Groq whisper-large-v3-turbo, batch mode.
_Avoid_: remote, online

**Local Backend**:
A **Transcription Backend** that runs the model on the user's machine via Whisper.NET (whisper.cpp under the hood). Picks the best available runtime in order CUDA → Vulkan → CPU; the first-run wizard downloads the chosen ggml model into `%APPDATA%\SpeechToText\models\`.
_Avoid_: offline, on-device

## Relationships

- Each machine selects exactly one **Transcription Backend** via a per-machine config file.
- One Idle → **Recording** → **Transcribing** → **Pasting** → Idle traversal is exactly one **Dictation**.
- A **Dictation** is handed to the configured **Transcription Backend** when **Recording** ends. The text returned by the **Transcription Backend** is pasted verbatim — including whatever punctuation and casing the backend produces — into the window that was focused at the moment **Recording** ended. (See ADR-0004 for why the original Post-Processor / Spoken Command layer was removed.)
- A **Hotkey** tap during **Transcribing** or **Pasting** starts a new **Dictation** that is queued behind the in-flight one — nothing in flight is cancelled.
- **Esc** during **Recording** aborts the current **Dictation**; no audio is sent to the **Transcription Backend** and no text is pasted.
- A **Dictation** that produces empty or whitespace-only text, or fails in **Transcribing**, is silently dropped (no paste, no clipboard touch); the tray icon flashes the error state briefly.
- The **Recording Indicator** is shown whenever any **Dictation** in the queue is in the **Recording** phase, and hidden otherwise. This is independent of the tray icon, which reflects the front-of-queue phase only — so a queued **Recording** behind a still-**Transcribing** front dictation shows the **Recording Indicator** while the tray icon continues to show **Transcribing**.

## Example dialogue

> **User:** "On my desktop I want to dictate locally — it has a GPU."
> **Dev:** "Got it — your desktop's config sets the **Transcription Backend** to **Local Backend**. Same **Hotkey**, same **Dictation** lifecycle, just a different backend resolving the audio."

> **User:** "I tapped the **Hotkey** but realised I was about to say the wrong thing."
> **Dev:** "Hit **Esc** while you're still in **Recording**. The **Dictation** aborts, nothing is sent anywhere, you're back to Idle."

## Flagged ambiguities

_(none yet)_
