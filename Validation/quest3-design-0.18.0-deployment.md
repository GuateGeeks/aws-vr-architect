# Current-design deployment — 0.18.0

Installed on Quest 3 on October 4, 2026. The preceding 0.17.0 APK was built before the later holographic workshop UI and solid 3D AWS emblem source changes. Checking its version number alone did not detect that mismatch.

The current design is now version 0.18.0, Android version code 20. Unity 6000.6.3f1 built the Android ARM64/Vulkan APK with zero errors and nine warnings. Its APK signature verified, `adb install -r` succeeded, and launch returned `Status: ok` with a cold start. Existing app data was preserved.

The source fingerprint in `design-0.18.0-source-hashes.json` was checked after building and matched every recorded file. The installed `base.apk` SHA-256 matches `Builds/Quest3/GuateGeeksAWSVR-Quest3-0.18.0.apk` exactly:

`88991e10cae9d8e6a02735af9c84b43336932d51f75ce8c44c777419e941dba9`

The current source's validation reports show 74 EditMode and 53 PlayMode tests passing. Copies are `design-0.18.0-EditMode-results.xml` and `design-0.18.0-PlayMode-results.xml`. Build, preflight, metadata, signature, installation and runtime checks are in `quest3-*-0.18.0.txt`.

The device reported version 0.18.0 and a running process. It was asleep at verification, so on-headset visual acceptance remains pending. Recognizable differences include raised 3D AWS emblems, glass panels, quiet secondary buttons, selection context actions and glowing connections.
