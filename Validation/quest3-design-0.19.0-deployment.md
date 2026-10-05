# Current-design deployment — 0.19.0

Installed on Quest 3 (serial 2G0YC5ZG9J06XL) on October 5, 2026. This build adds the following changes since 0.18.0:
- AWS Community Day Guatemala logo in the title window
- volcano horizon, floor light and ribbons
- plated gloves and emitter controllers
- the room refresh: pillars and arches, bulkhead, table, floor spokes, stands and slim status bar

- **Build:** version 0.19.0, Android versionCode 21. Unity 6000.6.3f1, Android ARM64/Vulkan, 0 errors and 9 warnings (`quest3-build-0.19.0.txt`).
- **Source check:** the fingerprint in `design-0.19.0-source-hashes.json` (104 files) was unchanged after the build.
- **Tests:** 74 EditMode and 53 PlayMode tests pass on the deployed source (`design-0.19.0-*-results.xml`).
- **Install:** run from the open editor with the new fixed `install-quest` command (`Assets/GuateGeeks/Editor/QuestInstall.cs`, Unity's bundled adb). `adb install -r` succeeded, which preserves app data. The launch returned `Status: ok` with a cold start.
- **Installed hash:** the installed `base.apk` SHA-256 equals `Builds/Quest3/GuateGeeksAWSVR-Quest3-0.19.0.apk`:
  `d037cb1ca8190ed66b45eea1a3aac5e41ad69e45c4a0324cc37dfab244b3a668`
- **Runtime (`quest3-runtime-0.19.0.txt`):** process running, installed version 0.19.0. The headset was asleep (`mWakefulness=Asleep`), so on-headset visual acceptance and frame timing are still pending.

To repeat: write `build-quest`, then `install-quest`, then `quest-status` to `Validation/editor-command.txt`.
