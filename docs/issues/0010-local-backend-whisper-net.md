# 0010 — Local Backend via Whisper.NET

**Type:** AFK
**Source:** `docs/PRD.md` (user stories 11, 12, 13; ADR-0001)

## What to build

**LocalBackend** implementation of the **TranscriptionBackend** interface using Whisper.NET (whisper.cpp under the hood).

- Implements the same interface as **CloudBackend**: `TranscribeAsync(byte[] pcmAudio, string initialPrompt, CancellationToken ct)`.
- Loads the model file specified by `ConfigStore.LocalModel`.
- Forwards `initialPrompt` to Whisper.NET so **Spoken Commands** are biased to be emitted as literal words (same prompt shape as **CloudBackend**).
- Provides a **model-download helper** used by the first-run wizard (slice #0009): downloads the chosen model `.bin` to a known per-machine location (e.g. `%APPDATA%\SpeechToText\models\`), with progress reporting and a size or checksum verification step before marking the model ready.
- Detects available runtimes (CUDA → Vulkan → CPU) and selects the best one. The first-run wizard surfaces only model sizes viable for the detected runtime.
- The boot path picks the backend from `ConfigStore.TranscriptionBackend`. Switching backends requires a restart (per ADR-0001).
- If the configured runtime is unavailable at boot (e.g. CUDA missing after a driver change), log a clear error and prompt the user to re-run the wizard or switch to Cloud.

## Acceptance criteria

- [ ] **LocalBackend** transcribes a 16 kHz mono PCM clip and returns text quality comparable to **CloudBackend** on a short benchmark sample.
- [ ] `initialPrompt` is forwarded to Whisper.NET; **Spoken Command** keywords appear as literal words in output (sanity-checked end-to-end through the **PostProcessor**).
- [ ] Model download has visible progress, supports cancellation, and verifies file integrity before marking complete.
- [ ] When the chosen runtime is unavailable at boot, the app logs a clear error and prompts the user to reconfigure.
- [ ] No audio is written to disk at any point.
- [ ] Switching the configured **Transcription Backend** in **ConfigStore** takes effect after restart only (consistent with ADR-0001).

## Blocked by

#0008
