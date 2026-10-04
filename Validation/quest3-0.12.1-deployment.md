# Quest hotfix 0.12.1 / versionCode 14

## Report and correction

The user confirmed that voice worked in 0.12.0, but “muévelo aquí” failed to find a valid destination while pointing at the table. The earlier generic targeting question was clarified as a destination failure, not evidence of selecting the wrong component.

Reproduced cause: voice destination projection used the hologram plane at y=1.5. A downward ray originating below that plane could not intersect it forward, although it clearly hit the visible table at y=0.74.

The patch projects onto the visible tabletop, maps the resulting x/z to hologram height, and shows a destination marker. Floating buttons preserve click behavior but allow explicit voice targeting of the table behind them. Stable destinations from the current speech turn survive up to ten seconds of lowered-hand delay; later unrelated turns and conflicting two-hand destinations do not silently reuse them. Live feedback no longer displays a stale frozen destination from the previous utterance.

## Validation

- 56 backend tests passed.
- 59 Unity EditMode tests passed, including downward rays at normal controller heights, transformed workspaces, delayed acceptance, stale preview, and conflicting destinations.
- 39 Unity PlayMode tests passed, including an actual scene ray pick, visible destination marker, lowered hand, successful move, and marker removal when ATLAS is disabled.
- Total: 154 regression tests.
- Visual evidence: `32-atlas-tabletop-destination.png`.
- The Realtime transport, model, turn detection settings, and backend source are unchanged from 0.12.0; no new paid audio test or backend deployment was required for this local geometry fix.

## Device installation

Release `Releases/0.12.1-20261004T082146Z` built successfully with zero errors, verified APK signature and Android ARM64 identity. All 13 original artifact hashes and 385 archived source hashes verified.

Installation on Quest `2G0YC5ZG9J06XL` succeeded with preserved app data. Launch returned `Status: ok` and `LaunchState: COLD`. Package inspection confirms version 0.12.1 / code 14, microphone permission granted, and an active process. No fatal exception appeared in the inspected startup log. Physical acceptance of the corrected destination behavior remains pending after installation.

At 02:24:03 local time the headset logged successful lab initialization. Four automatic speech turns were observed between 02:24:57 and 02:25:19, with three first-audio measurements of 611, 612 and 639 ms. `quest3-0.12.1-check.png` shows the running lab and speaking ATLAS; the ray in that frame targets Lambda, so it does not establish free-table destination acceptance. The user was reminded to wait for the AQUÍ marker over empty tabletop before issuing the move.
