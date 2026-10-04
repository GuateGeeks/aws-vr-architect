# Quest 3 release 0.12.0 / versionCode 13

## Verified before installation

- AWS backend updated successfully on 2026-10-04; the live broker helper verified deployed source and issued fresh ephemeral Realtime sessions. Permanent credentials remain in Secrets Manager.
- 56 backend tests, 55 Unity EditMode tests and 38 Unity PlayMode tests passed (149 regression tests).
- The final live native WebRTC test passed three automatic speech turns: Fast English, Natural English, and Natural Spanish correction. The Spanish fixture requested 50%, then corrected to 75%; exactly one size action at 0.75 was observed.
- Semantic eagerness `auto` split the correction in earlier attempts. The app's Natural mode now sends `low`; the broker's initial default is overridden when the data channel opens. Fast uses server VAD with a 650 ms silence threshold.
- Captions persist across tool continuations and reset for the next user turn. Regression coverage includes this behavior.
- Visual review: `31-atlas-compact-feedback.png` shows the compact panel above the work area with catalog and table controls accessible.

## Sample timings

See `voice-turn-comparison.md`. End of synthetic fixture to commit / commit to first audio:

- Fast English: 844 / 553 ms.
- Natural English: 3587 / 621 ms.
- Natural Spanish correction: 613 / 595 ms.

These are single observations, including transport and fixture timing. First audio may precede tool completion. They are not a performance guarantee or headset microphone measurement.

## Installation and physical acceptance

Release: `Releases/0.12.0-20261004T075619Z`. Android build succeeded with zero errors. Signature and ARM64 identity verified. All 13 original release artifact hashes and 384 archived source hashes verified.

Installed successfully on Quest serial `2G0YC5ZG9J06XL` and launched with `Status: ok` / cold start. Android package inspection confirms 0.12.0, versionCode 13, microphone permission granted, and a running application process. Existing app data was preserved.

Physical acceptance still requires wearing the headset: select a component by pointing and voice, resize it, point at free table space to move it, connect a pointed pair, interrupt a preview, and undo. The live synthetic test does not verify hand tracking accuracy, room noise, echo, or headset comfort.

## Follow-up device evidence

The installed app's device logs show six automatic microphone speech turns on 2026-10-04 between 02:03:06 and 02:04:24 local time. Five corresponding first-audio measurements were 819, 583, 611, 890, and 570 ms after acceptance, with semantic turn detection. The remaining turn has no matching first-audio measurement in the inspected log; no success is inferred for it.

The app lost focus at 02:04:50. During the follow-up check the Quest reported `mWakefulness=Asleep`, system microphone muted, app microphone permission granted, and the same 0.12.0 process running. This confirms real headset hands-free input and response events before sleep. It does not establish that spatial commands selected the objects the user intended; that confirmation remains pending.
