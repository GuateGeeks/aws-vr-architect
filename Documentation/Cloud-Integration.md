# GuateGeeksAWS2026 integration — 0.6.0

## Connect entirely inside Quest

1. Open **CONEXIÓN AWS → Configurar conexión**. The public endpoint and service username `quest-demo` are prefilled and editable.
2. Obtain the current **six-character**, case-sensitive password from the backend's `AuthSecret` in AWS Secrets Manager using the operator account. The current update preserves this credential.
3. Select the password field and enter it with the VR keyboard. Select slot 1–3 and optionally enable **Recordar y reconectar**.
4. Select **Probar y conectar**. Only the authenticated session and catalog are read; no AWS workload is created.
5. After **AWS · SLOT 1** appears, use **Validar diseño**, then **Desplegar / retomar AWS** and its explicit creation confirmation when you want real resources.

The headset must be on the network permitted by `AllowedCidr`. No USB, adb or running PC is needed for connection. Without a remembered profile the lab opens in demo; a failed remembered AWS connection remains disconnected and never presents mock success.

## Remembering and forgetting

The public profile contains only endpoint, username, slot and preferences. On Quest, the password is encrypted using AES-GCM and an AndroidKeyStore key, stored in the application's private `getNoBackupFilesDir()`. The endpoint, username and slot are authenticated with the ciphertext. Cryptographic operations run off the Unity main thread. This does not assume hardware-backed protection on every device.

**Recordar y reconectar** is opt-in and saves only after both session and catalog succeed. Restart reconnects but does not restore or deploy an architecture automatically. **Desconectar** stops requests and disables startup reconnection until an explicit connection. **Olvidar credencial** deletes ciphertext and key. Neither deletes AWS resources. Editor/desktop sessions are memory-only.

A missing key, altered file, 401 or invalid profile requires configuration again. 403 identifies a network restriction. Change of endpoint, account or slot requires entering the password again. Passwords and Authorization headers never go in profile JSON, PlayerPrefs, graph checkpoints, logs or APK.

## Migration and distribution

`tools/connect_quest.py` is only a legacy 0.3 bootstrap. Version 0.4 does not read its credentials and deletes any leftover bootstrap on startup. Do not use the script for new installs.

The development APK keeps the previous debug certificate for an in-place update. Installing this transition build may use USB; normal authentication afterward does not. Fully wireless installation/updates require the private Meta channel described in [Wireless-Distribution.md](Wireless-Distribution.md). The release build command is prepared; publication requires the owner's Meta app and signing configuration.

## Contract and validation

**Recuperar diseño AWS** restores the last checkpoint for the matching endpoint/slot. It contains the graph and stack ID, never a credential. Restoring does not deploy. Confirming **Desplegar / retomar AWS** consults or creates that design; if the prior slot was deleted, confirmation can create it again. **Dejar de observar AWS** stops local polling, not CloudFormation.

Use **Slots / inspección** on the main toolbar, then **Slot 1**, **Slot 2** or **Slot 3**. This reads the shared AWS slot independently of the current local design. **Actualizar slot** refreshes its state. Select a resource to see its physical AWS identifier.

- **Lambda → Inspeccionar logs** reads CloudWatch events from the last 15 minutes at refresh. These are real log records, not packet tracing. Select another Lambda separately for its logs.
- **DynamoDB → Inspeccionar ítems** reads an eventually consistent sample of at most 10 items per request. JSON preserves DynamoDB types (`S`, `N`, etc.). The view is read-only and does not save data in the visor.
- **Texto / registro** browses the returned text, 12 lines per VR page. **Más desde AWS** fetches another backend page, including after an empty page with a continuation cursor. **Actualizar** starts a fresh query. Cursors expire after 15 minutes. Individual records are capped at 4096 characters and visibly marked when truncated.
- **Limpiar slot… → Sí, eliminar slot N** deletes that exact deployed architecture, including DynamoDB data, workload logs and all S3 object versions. The confirmation identifies the slot, endpoint, region and immutable stack ID. The backend rejects a slot that has been replaced after inspection. Local design and saved library are preserved.
- **Dejar de observar** only stops waiting; AWS continues deleting. Refresh the slot to see completion or failure. Failed/uncertain deletion is never silently repeated, except an explicit backend request to resume an incomplete S3 purge against the same confirmed identity.

**Limpiar** on the design toolbar still clears only the local design table. Slots / inspección requires an AWS session and works over Wi-Fi without the connect script. The operator CLI remains available as a fallback.

`AwsCloudApi` uses UnityWebRequest, session/catalog checks, local capability checks, server validation, immutable slot creation/polling and event submission. It verifies both graph fingerprint and stack identity before displaying status or invoking an event. Missing node status remains provisioning; only `finished && success && CREATE_COMPLETE` completes a deployment successfully. API Gateway is limited to HTTP API; local validation rejects fan-out and S3/FIFO combinations unsupported by the backend.

Regenerate offline fixtures from the actual Lambda routes before running Unity contract tests:

```powershell
# From GuateGeeksAWS2026; AWS clients are mocked inside this exporter.
./.venv/Scripts/python.exe tools/export_unity_contract.py --output ../GuateGeeksAWSVR/Assets/GuateGeeks/Tests/Editor/Fixtures/backend-contract.json
./.venv/Scripts/python.exe -m unittest discover -s tests -v
```

Run Unity EditMode and PlayMode suites through the existing validation helper. Tests inject a transport with these real-route response fixtures, so they create no AWS resources. They do not establish live TLS/network compatibility or prove physical Quest interaction. A live smoke test remains necessary after a backend is deployed.
