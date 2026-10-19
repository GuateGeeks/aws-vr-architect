# Unity 0.24.4 recovery continuation

## Integrated project

The workspace now contains a compilable reconstruction of the production quetzal and hand interactions on top of the surviving source. The version is **0.24.4-recovery.1**, with Android versionCode **31** reserved for a future rebuilt APK. The original production 0.24.4 APK/versionCode 30 is unchanged.

### Quetzal

- Added `QuetzalArm` and `QuetzalSenses` using the production class layout/public API: separate left/right tracking, articulated fingertip/palm probes, pointing gestures, controller rays, estimated forearms, velocity, and explicit busy-hand suppression.
- Reconstructed the production seven-state lifecycle: Resting, Patrol, Approach, Landing, Perched, Excursion and Leaving. An offered arm or sustained pointing summons the bird; a stable arm supports landing. Loss of tracking, steep/fast arm motion, or revoked permission releases it. The supporting forearm is marked with a small ring.
- Added per-arm remembered permission using the recovered `GuateGeeks.Quetzal.Perch.v1.` preference prefix and exposed `PerchAllowed`, `SetPerchAllowed`, `TogglePerch` and `ReloadPerchPreferences`.
- Rebuilt the flexible neck, separate eyes/blinks, eight animated crest segments, and gripping feet around the original body/wing/streamer geometry. Retained the surviving flight and time-corrected Verlet tail implementation.
- Added body-region touch classification: bill, crown, chin, back, breast, wings and tail. Gentle contact produces petting/curiosity responses; fast contact makes the bird flinch and leave. Supporting-palm contact does not count as petting.
- Added materialization/attention/touch shader controls and reconstructed procedural spatial calls. Audio is on its own child object so it cannot displace the simulation root.
- Added deterministic `Senses.Simulate`, `Advance`, `PreviewPerch`, and interaction counters for validation without a headset.

### Hands and panels

- Recovered the production `BeaconButton` approach/contact/withdrawal hysteresis and timing from the ARM64 instructions. Tap the opposite hand's dorsal emitter with the index finger to toggle that arm's perch permission. Its colour and a short label show the new setting.
- Beacon animation uses material property blocks, matching the production allocation strategy; it does not create a new cached material for every animated colour.
- Confirmed from production `MultiFingerTouch.Step` that **only the index finger presses UI** in 0.24.4. Non-index UI states reset each frame. All articulated fingertips and the palm remain available for quetzal contact.
- Added four panel grip edges, `IsGrip`, `TryGrabNear`, `MoveNear`, and removal of grip controls. A near pinch preserves the hand/panel offset and rotation; exclusive grab ownership prevents another hand taking the panel. Tracking loss ends the grab.
- Kept existing distant ray/pinch controls, collaboration object leases, wrist menus, panel pinning and saved layouts.

## Controls

With tracked hands, hold a forearm roughly horizontal below eye height, with the back of the hand upwards, and keep it still. The bird notices the offer and approaches. Sustained pointing at it also attracts its attention; keep an arm offered for landing. Touch gently with the other hand. Tap a dorsal light with the opposite index finger to allow/deny perching on that arm.

UI buttons and keyboards use the index fingertip. Pinch a panel grip edge to move it near your hand. Distant pointing and pinch remain available.

In desktop rehearsal, hold **1** or **2** to offer the left or right arm. Release to withdraw the arm. Existing Q/E altitude controls are unchanged. These keys simulate an offer, not physical touch.

Reduced motion hides the animated bird, consistent with the surviving behavior. Existing sound settings also mute its calls.

## Validation and build

The recorded validation passed **101 EditMode tests and 68 PlayMode tests**. The Windows build succeeded with **zero errors** (three existing warnings). The validation fingerprint confirmed that the Unity source, package/settings files and Quest preload references remained unchanged during that test/build run. See `unity-reconstruction-validation.json` for the verified timestamp and image path. An October 18 rerun stopped before testing because the Unity Licensing Client could not connect; the last complete test reports are listed below.

Run `Tools/Test-UnityRecovery.ps1` with the installed editor. It runs offline EditMode/PlayMode suites, renders a perched-bird image, builds a Windows rehearsal player, and verifies that the source/settings did not change during validation. It excludes the LiveOpenAI test category and does not deploy or invoke production AWS workloads.

Final authoritative outputs:

- `unity-edit-tests-final.xml` and `unity-playmode-verified.xml` (full logs remain in the local recovery folder)
- `unity-reconstruction-validation.json`
- `desktop-build-result.json` and `desktop-build.log`
- The fresh per-run quetzal image recorded in `unity-reconstruction-validation.json` as `quetzalPreview` (under `render-checks/`)
- `Builds/Recovery/GuateGeeksAWSVR-Recovered.exe` (keep its accompanying data/runtime files)
- `recovered-windows-player.zip` and `windows-player-manifest.json` (complete player package and integrity hash)

Tests cover approach/landing/perching, transformed lab coordinates, foot contact, tracking loss and permission revocation, busy/disabled arms, gentle and fast contact, controller input, beacon hysteresis, material allocation, near panel ownership, and the existing architecture/voice/UI workflows.

The installed editor is **6000.6.6f1**. Unity migrated the project/package settings from 6000.6.3f1, including OpenXR 1.18.0 and Meta OpenXR 2.6.1. These migrations are present in the workspace and package lock. Android Build Support, SDK/NDK and OpenJDK are installed. A locally signed Android demo APK built on October 18, 2026 with zero errors and eight warnings (`Validation/quest3-build.txt`). It has not been tested on a physical Quest 3 and is not the original production APK.

Release/install/signing helper defaults now point to 6000.6.6f1 and the monorepo's `backend` directory. The signing helper derives its default APK filename from the current project version. PowerShell parsing checks passed; Meta release signing and headset validation remain pending.

## Fidelity and remaining work

This restores working interactions, not a byte-identical recreation of the lost source or original binary. The public states, major interaction pathways, permission key, button thresholds, index-only UI policy and recovered APIs come from the APK. Some gesture thresholds, projector placement, motion timing, touch volumes, pose/geometry detail, procedural sound and shader transitions were rewritten to reproduce those pathways. They need physical Quest tuning against the original installed APK.

Production had more nuanced feather/hair shading, eye/head saccades, wing/foot balance, preening/stretching, body-region reactions and synthesized calls than this reconstruction. Some are approximated rather than fully reproduced. The expanded DynamoDB card reader and welcome/guide refinements have not been integrated; their recovered code remains in `decompiled/` and `native-disassembly/`. Existing baseline readers/guides continue to work.

`reconstructed-project-source.zip` is a portable snapshot of the current Unity project and restored backend. Its hash and included source hashes are recorded alongside it. Large raw recovery artifacts and installed tooling are excluded; retain the original production APK and earlier evidence archive separately.
