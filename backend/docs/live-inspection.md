# Inspection protocol v2

GET `/v1/deployments/{slot}/logs` or `/items` retains the required stackId and resourceId identity checks. New optional query fields:

| Field | Meaning |
| --- | --- |
| q | Case-insensitive substring, maximum 80 characters |
| level | Logs only: INFO, WARN or ERROR; empty means all |
| eventId | Exact correlation ID, 1–128 letters, digits, underscore or hyphen |
| cursor | Next page within one bounded sweep |
| resume | Logs only: begin a new incremental sweep; do not send with cursor |

Responses add inspectionVersion: 2, incremental, and resume. Each entry has a stable id, title, text, truncated, and available timestamp/level/eventId/nodeId/targetNodeId/stage/requestId metadata. Typed DynamoDB AttributeValue JSON preserves precision. `eventId` item queries use strongly consistent GetItem on the id key; general browsing uses Scan. The control role needs dynamodb:GetItem as well as Scan.

Logs use CloudWatch record IDs, overlap the last completed sweep by 30 seconds, and sign resume/page state with the existing secret. Tokens expire after 15 minutes and bind stack incarnation, node, mode and filters. Follow every cursor, including empty pages, before using resume. A client should periodically repeat the full window to catch delayed ingestion (VR does this every 120 seconds), merge by stable record ID, bound memory and pause while hidden. Queries remain bounded to ten returned records per page and 4096 characters per record. Unfiltered log window is 15 minutes.

Workload received/delivered/processed/failed JSON includes eventId and requestId. delivered appears only after the outgoing SDK call succeeds and includes targetNodeId. For asynchronous services, it means accepted by that service, not downstream completion. Failed calls emit ERROR/failed with correlation and error type, then rethrow. Original exceptions still drive normal retries. An absent delivered record does not prove a call failed because log ingestion may lag.

Deploy the updated control plane using the existing SAM workflow after verifying its account, region and parameter values. Existing workload stacks are intentionally not updated by this operation. Their Lambda instrumentation changes only through an explicitly requested cleanup and new deployment. Do not recreate occupied slots to roll out this API change.
