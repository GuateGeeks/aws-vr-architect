# Reviewed Lambda code API

All routes use the existing Basic authentication and source IP restriction. No permanent OpenAI or AWS credential is sent to the client. The client chooses a logical node ID and exact stack ID; the server resolves its physical function from the owned demo stack's CloudFormation metadata. Only ready `CREATE_COMPLETE` stacks and Lambda nodes are accepted.

| Method and route | Body/query | Result |
| --- | --- | --- |
| POST `/v1/code/validate` | `source` | Syntax/handler validation and SHA-256 `sourceHash`; no execution |
| POST `/v1/code/test` | `source`, `eventJson` | Dedicated tester output, bounded logs, `passed` and source hash |
| GET `/v1/deployments/{slot}/code` | Query `stackId`, `resourceId` | Source, revision, code SHA, update status and latest ten versions |
| POST `/v1/deployments/{slot}/code/publish` | `stackId`, `resourceId`, `source`, `revisionId`, `confirmed: true` | HTTP 202, accepted version/update state, `rollbackVersion` checkpoint |
| POST `/v1/deployments/{slot}/code/rollback` | `stackId`, `resourceId`, `version`, `revisionId`, `confirmed: true` | HTTP 202, chosen immutable source republished as active code |

Sources must be UTF-8 Python, <=8 KiB, a single `index.py` in an AWS-owned ZIP <=1 MiB, with a synchronous two-argument `handler`. Test events are JSON objects <=4 KiB. Validation compiles but never executes in the control Lambda. Test execution occurs in `CodeTestFunction` (Python 3.13, 128 MB, 3 seconds), whose IAM role permits only writes to its own log group. It has no lab workload or Secrets Manager permissions. It is internet connected and shares the account's Lambda concurrency; it is not a general hardened untrusted-code platform. The authenticated demo API is not a multi-tenant execution service.

Publishing checks `RevisionId` and `LastUpdateStatus`, preserves an immutable version of current code, then uses `UpdateFunctionCode(Publish=True, RevisionId=...)`. HTTP 202 means accepted; read until `updateStatus=Successful`, or handle Failed/unknown explicitly. Never retry an ambiguous write automatically. Optimistic conflicts return HTTP 409. Restore is code-only. Version pagination is bounded at 500 entries; code retrieval displays the most recent ten and marks a partial list.

The device's review button supplies confirmation; Realtime has no tool that can supply it. Authentication permits a caller to invoke the HTTP API directly, so this is an application confirmation boundary, not a separate server identity or approval service. Every caller shares the demo account/slots.

Code publication changes active `$LATEST` and creates CloudFormation drift. Stack replacement uses the compiler default, not the local draft. Keep handler environment destinations and correlation logging when you want existing graph behavior to continue.

`deployment/verify-code-authoring.py` checks the real authenticated API, isolated success and timeout, publish, stale revision rejection and exact source restoration on a newly created fixture in a free slot. It refuses occupied slots and only cleans its captured stack ID. It prints no credentials and writes a sanitized acceptance report.
