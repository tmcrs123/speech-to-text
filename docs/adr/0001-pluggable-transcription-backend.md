# Pluggable Transcription Backend with per-machine selection

## Context

The user runs the dictation tool on multiple machines with very different hardware. A workstation with a capable GPU can run Whisper locally with cloud-quality latency and full privacy; a laptop without a GPU cannot, but is online and can call a hosted API. A single hard-coded engine forces a bad compromise on at least one of those machines.

## Decision

Introduce a `TranscriptionBackend` abstraction with two concrete implementations:

- **Cloud Backend** — Groq `whisper-large-v3-turbo`, batch mode. API key stored via Windows DPAPI (CurrentUser scope).
- **Local Backend** — Whisper.NET (whisper.cpp under the hood). Model size (small / medium / large-v3-turbo) is chosen at first-run-wizard time per machine.

Each machine selects exactly one backend via a config file written by the first-run wizard. There is no runtime toggle, no auto-detect, and no fallback between backends — the choice is explicit and persistent per machine.

## Considered alternatives

- **Cloud only.** Simplest, but forfeits offline use, privacy on sensitive content, and zero-cost dictation on the user's powerful machines.
- **Local only.** Forces a GPU requirement on every machine; unusable on laptops without one.
- **Runtime toggle (tray menu / hotkey).** Adds UI surface, dual-init overhead, and a mental-model burden the user did not ask for. They want set-and-forget per machine.
- **Auto-detect (GPU present → Local, else Cloud).** Leaky abstraction. A machine with a weak GPU would silently pick Local and feel slow, with no obvious cause.

## Consequences

- The abstraction must be wide enough to accept either a synchronous local call or an async HTTP round-trip without leaking either model into the caller.
- First-run wizard must handle both branches (API-key entry for Cloud, model download for Local).
- The "queue concurrent dictations" rule (see ADR-0002) applies identically to both backends — the wait is just different in nature (network vs compute).
