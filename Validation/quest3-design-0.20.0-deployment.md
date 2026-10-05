# Current-design deployment — 0.20.0

Installed on Quest 3 (serial 2G0YC5ZG9J06XL) on October 5, 2026. Changes since 0.19.0:
- The back-wall screen (bulkhead) and the server racks are removed; the reactor floats free as ATLAS.
- Background animation: aurora curtains, drifting clouds and valley mist, rising data streams, meteors, a scan line on the volcanoes and periodic Fuego eruptions with embers. Light runs along the pillar seams, roof arches and crown ring, and the far aperture rotates.
- A digital quetzal (`DigitalQuetzal.cs`) flies through the lab and into the horizon every 30 s, alternating sides. It is hidden with reduced motion.
- GuateGeeks identity: the logo from `Branding/Asset 41.svg` appears on two floating signs flanking the reactor and in the table etch.
- The eyes mark (`Branding/eyes.svg`) appears as live 3D eyes (`GeekEyes.cs`) on both signs and in the reactor core, following the viewer and blinking, and as a watermark on every major panel.

- **Build:** version 0.20.0, versionCode 22. Unity 6000.6.3f1, 0 errors, 9 warnings.
- **Source check:** the fingerprint (`design-0.20.0-source-hashes.json`, 110 files) was unchanged after the build.
- **Tests:** 74 EditMode and 53 PlayMode tests pass. One earlier PlayMode run failed once because the editor lost focus during a diagnostics test, and passed on re-run.
- **Install:** `install-quest` succeeded, preserving app data, and the launch returned `Status: ok`.
- **Installed hash:** the installed base.apk SHA-256 equals `Builds/Quest3/GuateGeeksAWSVR-Quest3-0.20.0.apk`:
  `08c89832d0c80428aad2edc9c782a2eb810ad214b4ceb6aade9c7962c3ccaa8f`
- **Runtime:** see `quest3-runtime-0.20.0.txt`. On-headset visual acceptance and frame timing are still pending.
- **Renders:** `Validation/42-digital-quetzal.png`, `43-guategeeks-signs.png`, `01-lab-overview.png`.
