# Current-design deployment — 0.21.0

Built on October 5, 2026. Changes since 0.20.0 (see `Documentation/Evaluacion-UX-Sala-Compartida.md`):
- **Shared room for up to 4 people:**
  - Numbered, coloured floor stations around the table.
  - A personal console that turns to the station and compacts to arm's reach.
  - "Alinear a mi estación" for same-room alignment.
  - Snap turn is disabled while the room is shared, and a zone guard warns when you leave your circle.
- **Presence and locks:** built on `ICollabSession`. Teammate visors carry the GuateGeeks eyes, name tags, station colours and pointer beams. Objects another person holds cannot be selected or moved; they can still be used in a connection. "Simular 4 usuarios" adds three deterministic teammates for rehearsal.
- **Smoothness:**
  - Allocation-free material caches.
  - Link curves are rebuilt only when an endpoint moves.
  - Static colours replace per-frame hex parsing in the table traces and node states.
  - Fixed foveated rendering is enabled (the OpenXR feature for Android, at level 1).
  - Aim assist of 2.5 cm.

- **Build:** version 0.21.0, versionCode 23. Unity 6000.6.3f1, 0 errors, 10 warnings. The new warning is a benign serializer notice for `NodeView.LockedBy`.
- **APK:** `Builds/Quest3/GuateGeeksAWSVR-Quest3-0.21.0.apk`, SHA-256 `3c205ed0c743ab02849c826fddb3207502dd8e8fd4ee2f5ae836b65999acf176`.
- **Source fingerprint:** `design-0.21.0-source-hashes.json` (89 C# files).
- **Tests:** 74 EditMode and 56 PlayMode tests pass (3 new: shared room, simulated teammates and locks, aim assist and allocation-free beams).
- **Install:** installed on Quest 3 (serial 2G0YC5ZG9J06XL) on October 5, 2026 with `install-quest`, preserving app data. The launch returned `Status: ok` (cold start). The installed version is 0.21.0, versionCode 23.
- **Installed hash:** the installed base.apk SHA-256 equals `Builds/Quest3/GuateGeeksAWSVR-Quest3-0.21.0.apk`: `3c205ed0c743ab02849c826fddb3207502dd8e8fd4ee2f5ae836b65999acf176`. Log: `quest3-install-0.21.0.txt`.
- **Runtime:** the process is running (pid 29509) and the headset was asleep; see `quest3-runtime-0.21.0.txt`. On-headset visual acceptance, frame timing with OVR Metrics and the 4-person alignment rehearsal are still pending.
- **Renders:** `Validation/44-shared-room-settings.png`, `45-shared-room-console.png`, `46-simulated-teammates.png`, `47-shared-room-overview.png`.
