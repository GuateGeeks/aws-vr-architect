# Current backend — updated October 4, 2026

**October 4, 03:16 Guatemala — VR 0.13.0:** control stack UPDATE_COMPLETE, deployed source verified byte-for-byte. ATLAS has 15 tools including grouped workflow review, live diagnostic monitoring and Lambda draft/code-review actions. Code authoring routes, dedicated three-second test Lambda, and revision-bound publication/restoration are deployed. Real tests passed syntax validation, draft result 42, timeout, active code publication, stale revision rejection, exact-source restore and deletion of only the newly created fixture. All slots were then confirmed ABSENT. See [code acceptance](code-authoring-acceptance.json), [source/status verification](release-0.13.0-verification.json), and [API limits](../docs/code-authoring.md). Permanent keys remain in Secrets Manager. Historical notes follow.

**October 3, 22:33 Guatemala — VR 0.9.0:** deployed inspection protocol v2 (filters, correlated metadata, incremental log resumes, exact DynamoDB GetItem) and structured instrumentation for future workload deployments. CloudFormation is UPDATE_COMPLETE; the downloaded Lambda package matches the tested inspection/compiler/workload sources. Live session, authentication rejection, catalog and all three preset validations passed. All slots were confirmed ABSENT; no workload was created or deleted, so live event/inspection acceptance remains pending a new demo deployment. Existing password, source CIDR, demo prefix and budget parameters were preserved. See [read-only verification](release-0.9.0-verification.json) and [protocol](../docs/live-inspection.md). Historical notes below describe earlier releases.

**Historical wireless update (October 1):** control stack updated successfully; six-character Basic password verified without logging its value. Authenticated session, catalog, three preset validations and all three empty slots verified again. Retrieve the new value in AuthSecret; the previous password is obsolete. Quest 0.4.0 configures it inside VR.

**October 2 update:** read-only Lambda logs and DynamoDB items endpoints deployed, plus optional immutable stack identity guard on deletion. Unity 0.6 always sends the identity guard. The service password was preserved. Live acceptance creates a temporary Lambda → DynamoDB architecture in an empty slot 3, verifies the same event in real logs and items, tests rejection of the wrong deletion identity, and deletes only the test stack. See [inspection-acceptance.json](inspection-acceptance.json).

CloudFormation completed successfully in `us-east-1`. Live API checks passed after deployment; see [connection-check.txt](connection-check.txt).

**Update, October 1 at approximately 20:07 Guatemala:** the architecture subsequently created in slot 1 was inspected and deleted at the user's request. All three slots are now empty; the backend remains active. See [the cleanup report](demo-cleanup-2026-10-01.md).

| Setting | Value |
|---|---|
| Account | `590183968738` |
| Stack | `guategeeks-aws2026` |
| API URL | `https://9bc46tb7d6.execute-api.us-east-1.amazonaws.com/demo` |
| Region | `us-east-1` |
| Demo prefix | `ggawsday` |
| Allowed public source | `186.151.64.244/32` |
| Operator CLI profile | `awsday` in Ubuntu WSL |
| Browser login profile | `awsday-login` |
| Artifact bucket | `guategeeks-aws2026-artifacts-590183968738-us-east-1` |
| VR application | Quest APK `0.6.0`, versionCode `6`; installation/build evidence in the Unity Validation directory |

Live checks verified authenticated session access, rejection of unauthenticated requests, the service catalog, three free deployment slots, and server validation of all three presets. No architecture workload stacks were created by these checks. The previously created slot 1 architecture was cleaned up at 20:07 Guatemala. No workload resources were created during the wireless update.

**Quest connected successfully at approximately 19:51 Guatemala time.** The USB bootstrap was consumed, both temporary credential files were verified absent afterward, and the backend logged two HTTP 200 responses at 01:51:44 UTC on October 2. The user confirmed **AWS · SLOT 1** inside the lab. This verifies the real headset's session/catalog connection; this historical connection check predates the slot 1 architecture subsequently inspected and deleted.

The renewed browser session authenticated as the existing account's root identity. `awsday` uses a credential process pointing to `awsday-login`; no permanent access keys or new operator IAM identity were created. The backend itself uses its generated IAM service roles. Do not confuse this CLI profile with the Quest's Basic Auth service account.

SAM's shared artifact-bucket reference was stale. Deployment succeeded using the dedicated bucket above with public access blocked, AES256 encryption, and bucket-owner-enforced ownership. The unrelated shared SAM stack/reference was left unchanged. Reuse `deployment/deploy-current-backend.sh` for this account/profile; it verifies the account and updates the allowed source to the current public IPv4. Build current sources first if they change. This script intentionally targets this specific installation.

## Connect the Quest

Use **Ajustes → Configurar conexión** in Quest and enter the six-character service password from AuthSecret. Optionally enable **Recordar y reconectar**. The headset must use the allowed public egress IP. No USB, connect script or running PC is required for normal authentication or slot operations.

USB is used only to install the development APK. See [the VR integration guide](../../GuateGeeksAWSVR/Documentation/Cloud-Integration.md) for wireless authentication, protected credential storage, slot cleanup and read-only logs/items inspection.

## Recheck and cleanup

### ATLAS update · October 4, 2026

The control-plane stack reached `UPDATE_COMPLETE` for the 0.11.0 voice integration. The deployed broker now configures automatic server speech detection, 55-minute sessions, and the local component, connection, visual-size and UI action tools. Long-lived OpenAI credentials remain in the existing Secrets Manager secret; the Quest receives only temporary session credentials. The shared 12-session/hour quota remains active.

The live native WebRTC test passed two synthesized speech turns in one session, including transcription, context tool calls and audio-response transcripts, without a manual commit. The test verified that deployed `app.py` and `assistant.py` exactly matched the current backend source. No workload stack was created by this update or test. Quest installation and physical microphone acceptance are recorded separately in the VR project's Validation directory.

`tools/check_connection.py --profile awsday --region us-east-1 --stack guategeeks-aws2026` performs the same checks without creating workload resources. A network change requires updating the allowed `/32` through deployment before connecting.

The backend remains deployed. After the demo, use [the existing cleanup procedure](../AWS-SETUP-COMMANDS.md#10-cleanup--run-only-when-the-demo-is-finished): remove any architecture slots before deleting the control-plane stack. The dedicated artifact bucket is a separate resource and must also be reviewed/removed when no longer needed. Stopping the app or disconnecting USB does not delete AWS resources.

Current wireless implementation and acceptance evidence: [0.4.0 validation](../../GuateGeeksAWSVR/Documentation/Validacion-Conexion-0.4.0.md). The new password remains exclusively in Secrets Manager and opted-in encrypted headset storage.
