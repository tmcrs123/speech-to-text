# Installer ships a single fat bundle with all Whisper native runtimes

**Status: Accepted (2026-05-17).**

## Context

Shipping the app via an Inno Setup installer (per the v1 packaging decision) raises the question of how to handle the Whisper.NET native runtimes. The `src/SpeechToText.csproj` references three runtime packages:

- `Whisper.net.Runtime` — CPU fallback, small (~tens of MB after publish-time filtering of non-Windows targets).
- `Whisper.net.Runtime.Vulkan` — Vulkan GPU path, ~27 MB on disk before publish.
- `Whisper.net.Runtime.Cuda` — CUDA GPU path, ~494 MB in the NuGet cache (the actual win-x64 native binaries that land in `bin/` after publish are a subset of that, but easily 150–300 MB).

`WhisperRuntimeDetector` picks one of CUDA → Vulkan → CPU at runtime based on what the host machine supports. **At most one of the three is ever loaded** on any given machine, and Cloud Backend users load *none* of them. So a fat installer bundling all three is structurally wasteful: every Cloud user downloads hundreds of MB of native DLLs they will never load, and every Local user downloads two runtimes they will not select.

A naturally cleaner architecture exists — ship a small bootstrapper installer (~20–40 MB) with the managed app only, then let the first-run wizard download the chosen Whisper runtime DLLs into `%APPDATA%\SpeechToText\runtimes\<flavour>\` alongside the model files it already downloads. The app's runtime loader would be taught to resolve native DLLs from that directory.

We are choosing not to do that, for v1.

## Decision

The installer ships a **single fat `.exe` containing all three Whisper native runtimes**, produced by `dotnet publish -r win-x64 --self-contained` and packed by Inno Setup. There is one installer artefact per release, downloadable from GitHub Releases. Expected size: 200–400 MB.

The Whisper native DLLs are placed in the install directory alongside `SpeechToText.exe`, exactly as `dotnet publish` lays them out. No runtime-download path exists.

## Considered alternatives

- **Two installer flavours: "Cloud" and "Local-capable".** Cloud installer (~20–40 MB) omits Whisper native runtimes entirely; Local installer (~300+ MB) bundles all three. Rejected because it forces every user to consciously choose Cloud-vs-Local at *download* time, before the first-run wizard has explained the trade-off in app-specific terms. The wizard exists precisely to make that decision with full context; pushing it earlier degrades the UX. Maintaining two installer scripts is also a recurring per-release tax.

- **Small bootstrapper + first-run runtime download.** Architecturally the cleanest answer to the size problem: download only what the user will actually load. Rejected for v1 because it requires net-new infrastructure — a runtime downloader analogous to `WhisperModelDownloader` but for native DLLs, checksum verification for the runtime archives, the `NativeLibrary.SetDllImportResolver` plumbing so .NET P/Invokes resolve from `%APPDATA%\SpeechToText\runtimes\` instead of the app directory, and handling for the network-fails-mid-download case. On a single-user app whose download cost is paid once or twice in the lifetime of the project, that infrastructure is not yet earned.

- **Publish with trimming (`/p:PublishTrimmed=true`).** Saves a small amount of managed-assembly weight; does nothing for native runtime DLLs, which are the actual size driver. Listed only to dismiss it.

- **Drop CUDA from the default build and ship Vulkan-only as the GPU path.** Vulkan is roughly an order of magnitude smaller (~27 MB vs hundreds for CUDA). Rejected because the user's workstation is GPU-equipped and the CUDA path is the fastest option there — silently dropping it on machines where it would have been picked would be a real performance regression to dodge a download-size problem.

## Consequences

- The installer download is large (200–400 MB). This is acceptable for the current audience (the developer's own two machines, plus occasional word-of-mouth) but will get more visible if distribution ever scales beyond that.
- Every installed copy of the app carries two unused Whisper native runtimes on disk forever. On a CUDA-equipped machine that's ~30 MB of dead Vulkan plus a few MB of dead CPU runtime; on a Cloud-only machine it's the entire ~300 MB of native code.
- `WhisperRuntimeDetector` continues to operate as today — it inspects the host and picks from runtimes already on disk in the install directory. No new lookup paths.
- **This decision is the natural one to revisit if installer size becomes a real pain point** — distribution beyond a handful of machines, install-abandon-rate complaints, or a desire to publish to channels that care about download size (winget catalog quality signals, a future Store presence). The bootstrapper-plus-runtime-download model from the second alternative above is the documented next step; this ADR exists in part to make sure that direction is taken deliberately rather than re-debated from scratch.
- If we ever ship signed releases (see the installer Q5 decision to defer code signing), the signing tax applies to one large `.exe` rather than two flavours or a bootstrapper-plus-N-runtime-archives. Marginally simpler signing pipeline.
- The .NET publish must remain `--self-contained` for non-technical users (no .NET 8 Desktop Runtime install required). That adds ~70 MB on top of the runtime bundles but is independent of this decision.
