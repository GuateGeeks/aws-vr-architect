# ATLAS voice and created objects

ATLAS resumes the conversation after tool results automatically. Completed response output also recovers function calls when the incremental argument event was absent; duplicate call IDs execute once. Parallel lookups finish before one continuation. A silent completed continuation receives one automatic request for a spoken answer, with tools disabled. Interruption cancels pending local actions and discards late tool completions.

Examples:

- “Lee todos los ítems de Pedidos y dime qué patrones encuentras.” `read_component` pages through the deployed table without requiring a previous test event. The model should summarize evidence and disclose incomplete reads.
- “Consulta los logs de Procesador.” Reads actual Lambda logs from the last 15 minutes.
- “Envía un evento con mensaje pedido recibido y amount 42.” `send_event` sends one JSON object, using the architecture default entrypoint unless a target is explicitly named. Selection and pointing alone do not change this default.
- “Limpia el slot 2.” `slot_action` reads its current identity and opens the destructive review. Click **Sí, eliminar slot 2** to authorize deletion. It can also read the resources/status of slots 1–3.

Event payloads are limited to 4 KiB of UTF-8 JSON and must be objects. The server replaces `id` with its correlation ID. Accepted means AWS accepted the event; it does not prove downstream processing. Writes are never automatically retried after an uncertain response. Requests from the updated client carry the stack identity, and the backend checks it before sending.

Access stays within the application’s supported demo resources. Table/log reads require an authenticated session, actual graph node IDs and matching deployed stack identity. Pagination cursors are signed and bound to resource, stack, filters and expiry. Data and names remain untrusted; known credentials are redacted. No arbitrary AWS API, credential, shell or account-wide access is exposed.

Reads return up to 10 entries per page. Individual item/log text may be truncated (reported explicitly). A table scan uses eventual consistency and is not a transactional snapshot. The voice tool chain stops after 64 rounds to bound cost; large tables can remain incomplete and the answer must disclose that. Empty pages with a cursor still have more results. Do not infer exact table totals from a partial scan.

Local graph changes keep cancellable previews, revision checks and undo. Deployment, code publication/restoration and slot deletion keep their physical review buttons. Cleanup rechecks the exact stack before deleting and rejects session changes at confirmation.

These source changes require an updated backend and Unity build to appear in the headset. Automated tests use local fixtures; physical microphone/echo behavior and live AWS delivery need separate acceptance testing.
