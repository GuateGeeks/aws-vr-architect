# ATLAS · 0.10.0

This document describes versions 0.10.0–0.10.1. Current controls use [continuous voice in 0.11.0](Experience-0.11.0.md); the old push-to-talk workflow below is historical.

ATLAS is the first AI milestone: a Spanish-first architecture assistant with native OpenAI Realtime audio and text input. It can read the current design and selection, explain services, propose complete architectures, select a node, inspect the latest accepted AWS event, and open the usual deployment review.

## 0.10.1 microphone correction

Microphone permission and Android recording were active on the affected Quest. The previous sender played the live microphone clip through an AudioSource, coupling transmission to an independently advancing playback cursor and Unity's mixer. The new sender reads only recorded frames from the microphone ring buffer, queues bounded 10 ms mono PCM frames, and passes them to WebRTC on the audio thread. It does not send the application's mixed sound or play microphone audio locally.

While speaking, the **MIC** meter shows the input level and recorded duration. Silence, capture interruption and transmission failure produce distinct messages. **Enviar voz** drains the captured frames and waits for `input_audio_buffer.committed` before requesting a response. Granting microphone permission no longer cancels the recording request just because the Android dialog temporarily takes focus. Session suspension outside that dialog still disconnects.

The live test now sends synthesized speech through this PCM reader and native WebRTC and asserts that OpenAI transcribes the expected words, returns a context tool call, and responds. This verifies speech transport in the editor; physical microphone acceptance must still be checked on the installed headset using the meter and an actual spoken request.

## Using ATLAS

1. Connect to the backend in **Ajustes → Conexión AWS**. A deployed workload is not required for architecture assistance.
2. Open **ATLAS** from the architecture controls and select **Conectar IA**.
3. Select **Hablar**, grant microphone permission when requested, speak, then select **Enviar voz**. **Escribir** opens the VR keyboard instead. Recording stops after 45 seconds. The microphone is off between messages.
4. Try “Lee mi diseño y propón una API con Lambda y DynamoDB” or “Agrega una cola antes de esta Lambda”. Select a resource first to identify “esta”.
5. Read the generated change list using its page controls. **Aplicar propuesta** changes the local design; **Descartar** cancels it. **Deshacer IA** is available immediately after applying, until a later manual change.
6. Deploy using the existing review and explicit AWS confirmation. ATLAS cannot execute deployment or deletion itself.
7. After deploying and sending an event, select Lambda or DynamoDB and ask “Inspecciona el último evento de este recurso”. ATLAS reads the existing event; it does not send another test event. Data is limited to five entries of up to 1,200 characters each. Common credential patterns are redacted on a best-effort basis. Requested log/item excerpts are sent to OpenAI.

**Interrumpir** stops the current response and recording. **Desconectar** or closing ATLAS ends the session and discards pending proposals. Headset suspension also disconnects. After denying microphone access, typed input remains available; enable microphone access in Android app permissions to speak later.

## Behavior and limits

- Proposals use the existing service catalog and validators: up to 12 nodes and 36 links, valid settings and edges, no cycles. IDs and positions are preserved when possible. The application computes the change list rather than trusting the model's summary.
- A pending proposal expires when the architecture or layout changes. Pending manual property edits must be completed first. Applying a proposal never creates AWS resources.
- Inspection is a bounded snapshot of the last event, not a background AI monitor. Empty or delayed results do not prove failure. The normal inspection reader still updates live independently.
- Lambda source generation, code editing, testing, versions and aliases are **not included**. Those require the next authoring/deployment milestone.
- Each app session disconnects after 10 minutes or 90 seconds idle. A shared backend quota allows 12 session credentials per UTC hour, including failed provider attempts. This is an abuse guard, **not a hard spending cap**. The ephemeral token expires for new connections after 30 seconds, not after 30 seconds of conversation. OpenAI usage is billed separately from the AWS budget.

## Backend configuration

Deploy the SAM template with `OpenAISecretArn` referring to an existing Secrets Manager secret in the backend region. Accepted secret formats are JSON with `OPENAI_API_KEY` (or `api_key`) and a raw key string. Do not put keys in Unity assets, PlayerPrefs, deployment parameters or APKs. `OpenAIRealtimeModel` defaults to `gpt-realtime-2.1`; `gpt-realtime-2.1-mini` is also allowed.

The authenticated `POST /v1/assistant/session` route uses the existing Basic Auth and API source-IP restriction, an encrypted DynamoDB quota table, and narrowly scoped secret read access. Only an ephemeral credential is returned to the headset. Native Unity WebRTC posts an SDP offer to OpenAI and streams audio/events directly. Provider errors are sanitized.

Unity WebRTC 3.0.0 supports Android ARM64/IL2CPP. Optimized frame pacing is disabled as required by the package. Android microphone permission is requested when the user starts speaking. Audio rendering, microphone comfort and sustained Quest performance still need in-headset acceptance.

## Validation and release

Run `Tools/New-QuestRelease.ps1` with the pinned Unity editor open. It runs backend, EditMode and PlayMode checks, builds and verifies the APK, and archives source and reports.

The explicit `RealtimeLiveTests.NativeWebRtcCompletesToolAndAudioResponse` test is excluded from normal suites. The fixed editor command `assistant-live-test` waits for a fresh ephemeral broker ticket in `Validation/assistant-smoke-ticket.json`, consumes and deletes it immediately, then checks native WebRTC speech upload, input transcription, a real context tool call, response transcript and usage. Supply a synthetic 48 kHz PCM16 mono WAV at `Validation/atlas-voice-fixture.wav` saying “Read the current context. Then say one short sentence describing whether the design is empty. Do not propose changes.” The test opens no physical microphone. It requires a configured backend and consumes a short paid API response. No permanent key is used by the test client.

References: [OpenAI Realtime](https://developers.openai.com/api/docs/guides/realtime), [WebRTC connection](https://developers.openai.com/api/docs/guides/voice-webrtc), [Unity WebRTC requirements](https://docs.unity3d.com/Packages/com.unity.webrtc@3.0/manual/requirements.html).
