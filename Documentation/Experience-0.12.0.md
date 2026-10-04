# 0.12.0 — Point, speak, and see the result

Activate ATLAS once. Its compact, movable display stays visible when the main assistant panel is closed; the microphone stays active until you deactivate it. It pauses during credential entry, loss of application focus, or headset suspension.

## Pointing and voice

- Hold your hand/controller ray over a component briefly (at least 0.3 seconds), then say “haz este componente más pequeño.”
- Point to two components in order and say “conecta este con aquel.” If the references are ambiguous, ATLAS should ask which objects you mean.
- Refer to a recently discussed component, point at free table space, and say “muévelo aquí.” Movement preserves group spacing and rejects destinations outside the workspace or overlapping other components.
- “Pon este al 75%” changes that component's visual scale. “Pon todos al 50% y ordénalos” changes the whole table. Supported visual sizes are 50–125%; they do not change AWS capacity or deployment state.
- Speech captures a bounded pointing snapshot. References expire after 25 seconds instead of silently changing to whatever you point at later. Undo restores position and individual/global size together.

## Conversation and feedback

The main ATLAS panel offers **Natural** (semantic turn detection with low eagerness) and **Rápida** (650 ms silence detection). Both stream continuously without push to talk. Natural lets the service judge whether an idea is complete and gives hesitations more room; it can take longer for uncertain endings. Preferences persist locally.

The compact display shows listening, thinking, acting, speaking, completion, microphone problems, and pause states. Captions can be hidden independently of audio. Component edits briefly highlight their targets before execution. Speaking again or pressing **Detener** cancels pending work and playback while keeping the assistant enabled. Completed local edits can be undone. Manual edits made during the preview invalidate the pending voice edit.

Spanish/English switching and spoken corrections are supported by the assistant instructions. Actual interpretation depends on audio quality and the service; test pointing and interruptions while wearing the Quest. Preview is brief (0.7 seconds), so undo is the recovery path for a correction that arrives after an edit.

## Validation

Regression coverage includes stable/expired/ambiguous references, per-component scaling and persistence, movement bounds, grouped undo, interruption, stale previews, and playback state. Live synthetic speech validation compares both turn modes and checks a Spanish size correction. Measurements are individual observations, not a latency benchmark. See `Validation/voice-turn-comparison.md` and the release validation reports.

AWS deployment/deletion still uses the existing review and confirmation flow. This release does not add arbitrary Lambda code authoring.
