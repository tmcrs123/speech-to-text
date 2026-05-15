# 0007 — ConfigStore with DPAPI-encrypted Groq key

**Type:** AFK
**Source:** `docs/PRD.md` (user story 10; foundational for 8–11)

## What to build

**ConfigStore** module. Persists per-machine configuration in user-scoped app data (`%APPDATA%\SpeechToText\config.{toml|json}` — pick one format and stick with it).

Schema (start `schema_version = 1`):

- `transcription_backend`: `"cloud" | "local"`
- `groq_api_key_dpapi`: DPAPI-encrypted ciphertext, base64-encoded (cloud only) — encrypted via `ProtectedData.Protect` with `DataProtectionScope.CurrentUser`
- `local_model`: `"small" | "medium" | "large-v3-turbo" | …` (local only)
- `hotkey`: serialised chord descriptor (default `Ctrl+Shift+Space`)
- `max_recording_seconds`: integer, default `120`
- `start_stop_sounds_enabled`: bool, default `true`
- `auto_start_on_login`: bool, default `true`
- `input_device_id`: nullable device id (null = system default)
- `schema_version`: integer

The module exposes typed accessors per setting — callers never see raw JSON/TOML or raw ciphertext. The Groq API key accessor returns plaintext (decrypted on read, encrypted on write).

Replace the `GROQ_API_KEY` env-var lookup from slice #0001 with `ConfigStore.GetGroqApiKey()`. (Until slice #0008 supplies a UI, the developer populates the key by running a one-off setter from a dev tool or temporary CLI — surface a clear error if the key is missing.)

Behaviour on missing / corrupt config: return defaults, log a warning, do not throw.

Behaviour on concurrent writes: serialise internally so two near-simultaneous writes cannot corrupt the file.

**Unit tests required** (per PRD Testing Decisions).

## Acceptance criteria

- [ ] Round-trip of every setting (write → read) returns the original value.
- [ ] The Groq API key plaintext does not appear anywhere in the on-disk config file (verified by reading the raw file bytes).
- [ ] Decrypting on a different Windows user account fails cleanly (DPAPI CurrentUser scope).
- [ ] Missing config file on first read returns defaults, no exception thrown to callers.
- [ ] Corrupt JSON/TOML returns defaults, logs a warning, no throw.
- [ ] Two near-simultaneous writes do not corrupt the file.
- [ ] `schema_version` is written on save and read on load; future versions are handled by a documented policy (tolerate-or-fail-clearly).
- [ ] Unit tests cover every acceptance criterion above.

## Blocked by

#0001
