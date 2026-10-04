# 0.9.0 · Inspection, hands and guided missions

## Inspect an event

Open a deployed Lambda or DynamoDB table from Slots / inspección. The reader polls every three seconds while visible, pauses during text entry or other operations, and backs off on transient failures. Search uses the VR keyboard; Lambda logs can filter ERROR, WARN or INFO. Quitar filtros restores the full view. JSON formatting preserves original numeric text; incomplete/truncated JSON stays unchanged.

After sending an event, select Seguir último evento enviado in both readers. The same event ID connects the Lambda log to its stored DynamoDB item. Exact item lookup uses a strongly consistent GetItem. Unfiltered table browsing remains an eventually consistent, bounded scan. Logs retain distinct CloudWatch record IDs even when messages are identical. A pinned record survives refresh; Seguir recientes resumes following the newest records.

The version 2 backend issues signed continuation and incremental resume tokens. Log resumes overlap by 30 seconds; the client reconciles the full 15-minute window every two minutes and holds at most 100 records. CloudWatch delivery can be delayed. This is automatic polling, not a push stream. Filters reset pagination; tokens bind resource, stack incarnation and filters, and expire after 15 minutes.

Newly deployed workload Lambdas emit received, delivered, processed and failed records with eventId and requestId. A matching delivered record briefly marks its Lambda → target edge CONFIRMADO · AWS. This confirms the upstream service call was accepted, not that all downstream asynchronous processing has finished. Only the currently inspected Lambda and most recently sent event drive this indication. API/S3/queue ingress and CloudWatch associations do not claim observed traffic. Previsualizar flujo remains explicitly SIMULACIÓN.

Existing workload stacks retain their original Lambda code. Updating the control plane does not replace occupied slots; use the normal explicit cleanup/deployment flow when ready to install the new workload instrumentation. Search and formatting work locally with the older backend, but severity, event metadata, incremental queries and confirmed edges require backend version 2.

## Hands and recovery

Bring an index fingertip toward a button from its front, then touch briefly. Withdraw before touching again. Contact and pinch do not activate the same button twice. Pinch a nearby node to move it with the fingertip; far selection and controller input remain available. The reticle changes size/color as pinch pressure increases.

Face the left palm toward your head for a moment to reveal Menús, Guía, Inspección and Ajustes. Use the right index finger or a ray to select. The menu hides on tracking loss, system gestures and modal text entry. This geometry and gesture logic still require acceptance inside the physical Quest; editor tests cannot verify tracking quality or comfort.

## Guided first use

Guía opens a movable panel beside the catalog and appears on the first headset launch after this update. It preserves the current design. Its sequence is review → confirmed deployment → event → correlated logs/item → confirmed cleanup. The local route clearly identifies simulation. Cloud progress requires actual returned event IDs and slot status. Changing design or cloud connection resets the relevant progress. Closing the guide preserves its progress for the running session; Reiniciar guía starts over. Closing the app does not clean up AWS resources.

If a hand disappears, open the pinch and reacquire tracking before grabbing again. Recover misplaced panels with A + X or Palma → Menús. If authentication expires, reconnect through Ajustes → Conexión AWS. During network failure the reader retains its last data and identifies it as stale; reconnect and reselect the resource after an identity/permission error. Confirm slot identity before cleanup. The guide never automatically deletes resources.

## Versioned builds

Open this project in Unity 6000.6.3f1 with Android modules, exit Play Mode, install the sibling backend's requirements-dev.txt into its .venv, then run:

```powershell
./Tools/New-QuestRelease.ps1
```

The script runs backend tests/template checks and fresh Unity Edit/Play tests, builds ARM64/Vulkan, checks the APK identity/signature, and creates a new timestamped Releases folder. It preserves the APK, test/build reports, allowlisted source archive, individual source hashes and a SHA-256 artifact manifest. It rejects source changes during validation. It does not deploy AWS or install the headset. Review warnings in Validation/quest3-build.txt. These are locally signed sideload builds, not store releases; source/toolchain capture supports rebuilding, but does not promise byte-identical APKs across machines or signing keys.

Install a chosen artifact with `./Tools/Install-Quest3.ps1 -Apk <absolute-apk-path> -Serial <quest-serial>`. Compare its SHA-256 to manifest.json first. The installer preserves app data and does not automatically uninstall on failure. Keep an earlier release for recovery. Android may reject a lower version code; the safest rollback is to rebuild the earlier source with a higher version code using the same local signing key. Do not uninstall to bypass this without first considering saved designs and encrypted credentials.

After installation, physically verify direct touch, palm orientation, near/far grabs, text readability, tracking loss, reduced motion, network interruption, and a full AWS event/cleanup route. Headset acceptance and backend deployment are separate from the build report.
