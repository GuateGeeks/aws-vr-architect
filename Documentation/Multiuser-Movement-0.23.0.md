# Multiuser movement — 0.23.0

The reported snap-back has concrete causes in the client: it restored the committed graph immediately after sending a drop, and rebuilt every object when another participant changed the graph. The ownership client also discarded pending claims on unrelated snapshots, permitting duplicate claims to replace the lease token. Whole-graph operations rejected simultaneous drops on different objects because both used the same revision.

## Changes

- Keep the local candidate visible until its matching server receipt. Committed room state remains separate. A rejection, lost connection, or uncertain delivery reconciles to the server state.
- Reconcile snapshots into existing object views. Unrelated edits and membership/heartbeat updates preserve the object being held, selection, and links that did not change.
- Send a position-only `move` command, fenced by the live object lease. The backend merges into the latest graph, preserving concurrent edits to other objects. Definition edits and undo retain their existing revision checks.
- Track claim receipts individually, retry unanswered claims at a bounded rate, and retain the ownership token for repeated live claims. Wait for release confirmation before reusing a lease; retry a missing release response.
- Latch the intended object and grab offset while waiting for ownership in controller, hand pinch, and desktop input. Moving the pointer during the round trip does not switch the requested object.
- Piggyback live drag previews on the existing 10 Hz presence stream. The server validates the owner, lease, revision, sequence, and table bounds. Previews are transient; they do not change the persisted graph or operation history. Receivers reject older sequences and previews preceding a committed drop. An unrelated commit preserves the current preview.
- Interpolate remote movement with a 65 ms response time. Local held objects follow input directly. Remove full graph copies and JSON serialization from the per-frame equality checks.

## Validation

Backend: 108 tests passed. Unity EditMode: 86 tests passed. Unity PlayMode: 59 tests passed. Client and PlayMode test assemblies compile with Unity's bundled SDK. Regression coverage includes simultaneous drops, duplicate and expired leases, transient previews, out-of-order previews, unrelated commits during a grab/drop, receipt latency, and rejected-drop reconciliation without replacing the object view.

PlayMode uses injected transport messages; it is not a four-headset test. No headset was attached to adb during this work. Physical alignment, hand/controller feel, frame rate, and real network latency still need a multi-device acceptance run.

## Rollout

Client version: 0.23.0, Android versionCode 25. Deploy the backend before installing the new client: the new `move` command requires the updated room handler. Existing 0.22 clients continue using supported `op` commands.

`backend/deployment/deploy-movement-fix.py` defaults to a read-only plan and saves rollback ZIPs under ignored `Temp/MultiuserRollback`. With `--apply`, it replaces only `collaboration.py` in the existing ControlFunction and CollabFunction packages, uses Lambda RevisionId guards, and verifies the deployed source bytes. It does not update stack configuration or other packaged files.

After the user renewed the AWS login, the patch was deployed to `ggawsday-control` and `ggawsday-rooms`; downloaded source bytes match the tested module. The live test connected four independent WebSocket clients and verified stable duplicate-claim tokens, previews delivered to three peers without a persisted edit, both simultaneous drops converging on all four clients, ownership handoff after release, and rejection of a late move from the old owner. Test sockets were closed. Evidence: `backend/deployment/movement-backend-update.json` and `backend/deployment/movement-live-verification.json`.

Quest APK build succeeded with zero errors and ten warnings. Package metadata confirms 0.23.0 / versionCode 25, ARM64, minimum API 32 and target API 34. The immutable delivery copy is `Builds/Quest3/GuateGeeksAWSVR-Quest3-0.23.0.apk`, SHA-256 `20963875f676766ab8a98204033bfa244937633c0ea20f786fa065bf4fe9d2a7`. No headset installation or physical four-user evaluation was performed. Install this version on every participating headset to enable the client fixes.
