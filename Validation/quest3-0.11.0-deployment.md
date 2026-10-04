# Quest 0.11.0 deployment · 2026-10-04

- Release: `Releases/0.11.0-20261004T065355Z`.
- Package: `com.guategeeks.awsarchitectlab`, version `0.11.0`, versionCode `12`.
- APK built successfully: 0 errors, 8 warnings. ARM64 metadata and APK signature verified by the release script.
- Regression results: 56 backend tests, 51 EditMode tests, 33 PlayMode tests passed (140 total).
- Live OpenAI test passed two synthesized speech turns in a single native WebRTC session using automatic VAD, including transcription, context tools, and response transcripts. No manual commit or physical microphone was used in that test.
- All 13 release artifact hashes and 375 archived source hashes verified.
- Existing AWS control-plane stack updated successfully; deployed broker source matched local source during the live test. No workload resources created.
- USB installation succeeded on connected Quest 3. Cold launch returned `Status: ok`; device package inspection confirmed versionCode 12 and RECORD_AUDIO permission granted. Unity startup log confirmed the lab initialized.
- The initial wake-up diagnostic still reported system microphone mute. A later check reported `from system=false`, and the running Quest app logged `ATLAS audio: automatic speech turn accepted` at 00:56:12 and 00:56:46 local time. This confirms two automatic audio turns from the installed headset. No provider errors or app exceptions appeared in the filtered runtime log.
- A headset screenshot (`quest3-0.11.0-voice.png`) shows the AWS session connected and `ATLAS · ACTIVO` while its panel is hidden. User confirmation of the requested component-edit and resize outcomes remains pending; their behavior is covered by the regression suite.

The release manifest's `headsetAcceptance: pending` refers to complete user acceptance, not installation or the two device audio turns verified above. Its backend field describes the release script's scope; the deployment was performed separately during this task.
