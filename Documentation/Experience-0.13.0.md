# 0.13.0 — Plans, live evidence, and Lambda code

ATLAS keeps the existing continuous, interruptible voice conversation and tabletop targeting from 0.12.1. Activate once; deactivate explicitly to stop the microphone. The new tools use the same graph IDs and revision checks as the UI.

## Architecture workflows

Ask: “Prepara una API con Lambda y DynamoDB, acomódala al 65% y abre la revisión.” ATLAS can stage the complete graph, visual size, arrangement and design review together. Review the changes, then apply by button or explicit voice request. A single **Deshacer IA** restores the previous graph, positions and size. Later manual edits invalidate an old proposal or its dedicated undo. Applying the plan does not deploy AWS resources or publish code.

## Live diagnostics

Select a Lambda or table and click **Diagnóstico vivo**, or ask “Monitorea la Lambda y la tabla.” The monitor follows the latest event already sent from this session, reads up to four resources and updates automatically after each round plus five seconds. It never sends an event itself. A sample can be partial or delayed; no records does not prove failure. Counts describe observed records, errors and deliveries, not an inferred end-to-end success rate.

Ask “¿Qué muestra el diagnóstico?” for an explanation of the current bounded, redacted evidence. Changed evidence is supplied to ATLAS at most every fifteen seconds for a subsequent requested answer; the monitor does not produce unsolicited speech. The panel stops on close, explicit stop, session/design change, focus loss, assistant deactivation or a ten-minute limit. It preserves the last evidence and its timestamp. Logs/items remain untrusted data. Redaction is best effort; do not put secrets in source or event payloads.

## Lambda code studio

Select a Lambda and click **Código Lambda**, or ask ATLAS to open its code. The editor supports one Python 3.13 `index.py`, up to 8 KiB, with `handler(event, context)`, the standard library and bundled boto3. Local drafts are separate from the architecture graph and persist on the device. They are plain local files; they are not a secret store.

1. **Cargar AWS** reads the selected deployed function and its current revision. This replaces the local source; **Deshacer borrador** can recover the preceding draft. AI reads preserve an existing edited draft.
2. Ask for a change, or edit/insert/delete individual lines with the VR keyboard. It preserves indentation, supports case switching and Python punctuation. Pages wrap long source/diff lines without hiding their content.
3. **Validar** checks syntax without executing. **Evento JSON** sets the input for **Probar borrador**. Draft tests use a separate three-second Lambda with permission only for its own logs. They do not invoke the deployed function or validate its real resource access. Internet connectivity exists; this is not a network-disabled sandbox. A successful test applies only to that exact source and event.
4. **Revisar publicación** shows the code changes and test state. **Confirmar código en AWS** is a manual button that changes the active `$LATEST` function and publishes an immutable version. ATLAS can open the review but cannot press this confirmation through a tool. A matching AWS revision is required; conflicting edits are rejected. Updates are polled to completion; ambiguous responses must be checked with **Cargar AWS**, not automatically retried.
5. The **Versiones** tab shows the last ten published versions. Choose a version, then **Revisar restauración** and manually confirm. Selecting another version invalidates the previous review. Restoration publishes the selected source as new active code and preserves a checkpoint of the code being replaced. It restores code only, not environment variables, IAM, triggers or configuration.

Code updates intentionally create drift from the original CloudFormation inline code. Recreating a demo stack uses the compiler's default handler, not a device draft. Keep the existing handler's destination environment handling and event-correlation logs when editing unless you intend to replace that behavior. All authenticated devices still share the lab's three deployment slots; this is a demo authoring workflow, not multi-tenant production isolation.

## Verification

Regression coverage includes grouped workflow undo, stale source hashes, indentation, review/version binding, interrupted requests, source/event test binding, event correlation, redaction and automatic diagnostics polling. Backend tests cover stack ownership, package constraints, immutable checkpoints, stale revisions, dedicated tester permissions and test timeouts. Live publication/restore checks use a new disposable stack in an empty slot and delete only that fixture. Release reports record actual results; headset usability and pointed destination intent still require a wearer check.
