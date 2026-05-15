# Issues

Local issue files. Each one is a tracer-bullet vertical slice of the v1 PRD (`../PRD.md`).

When an issue tracker is configured (`/setup-matt-pocock-skills`), these should be migrated one-to-one and labelled `ready-for-agent` (except `#0008`, which is HITL).

## Slice dependency graph

```
#0001 walking-skeleton-tracer
  ├── #0002 esc-abort-max-duration-cutoff
  ├── #0003 postprocessor-spoken-commands
  ├── #0004 window-targeter-clipboard-paster
  └── #0007 config-store-dpapi
       └── #0008 settings-window-wpf  (HITL)
            ├── #0009 first-run-wizard
            ├── #0010 local-backend-whisper-net
            └── #0011 auto-start-on-login

#0002 + #0003 + #0004 → #0005 dictation-orchestrator-state-machine-queue
                           └── #0006 tray-icon-and-start-stop-sounds

#0007 → #0011 (also depends on #0008)
```

## Order of execution

A reasonable single-developer order:

1. #0001 → tracer bullet
2. #0002, #0003, #0004 → in parallel or any order
3. #0005 → state machine + queue
4. #0006 → tray + sounds
5. #0007 → ConfigStore
6. #0008 → settings window (HITL design review)
7. #0009, #0010, #0011 → in any order after #0008

## HITL slices

- `#0008` — settings-window layout / UX design review before merge.

All other slices are AFK.
