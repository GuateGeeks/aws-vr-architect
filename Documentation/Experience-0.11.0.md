# ATLAS continuous voice and lab actions · 0.11.0

Connect the AWS API in Settings, open ATLAS, and press **Activar ATLAS**. Grant microphone permission and speak normally. There is no push-to-talk or send-audio button. The input meter shows whether the headset is receiving a signal. **Desactivar ATLAS** stops capture and cancels pending work immediately. Closing the ATLAS panel only hides it; the dock continues to show **ATLAS · ACTIVO**.

Audio is continuously sent to OpenAI while enabled and the app has focus. Speech detection automatically ends each turn after a short pause. Speaking again interrupts the current response while keeping capture open. The credential form pauses capture. Headset suspension closes the transport; returning to the app reconnects while the enabled intent remains set. Sessions renew after 55 minutes, with bounded reconnect backoff. The backend retains its shared limit of 12 session credentials per hour. Enabling again is required after app restart or denied/unavailable microphone permission. Silence does not turn the assistant off.

## Voice actions

Try these requests in Spanish or English:

- “Agrega una Lambda llamada Procesar pedidos con 256 MB.”
- “Selecciona Procesar pedidos y cambia su memoria a 512 MB.”
- “Conecta la API a Procesar pedidos.”
- “Desconecta esa Lambda de la cola.”
- “Elimina este componente del diseño.” / “Deshaz ese cambio.”
- “Abre las relaciones de este componente.” / “Revisa el diseño.”
- “Pon los componentes al 50% y ordénalos en la mesa.”
- “Abre ajustes.” / “Guarda el diseño.” / “Previsualiza el flujo.”
- “Abre la revisión para desplegar.”

Small explicitly requested edits use the same local graph actions, compatibility rules and undo history as the UI. Context supplies node IDs, revision, valid settings, selection, visual size and available UI actions. Stale revisions, grabbed objects, unfinished manual edits, invalid settings, unsupported links, cycles, and the 12-component limit are enforced in the app. Full architecture proposals still require explicit application by button or voice request. Deploying/deleting AWS resources still requires the existing manual confirmation; no voice tool bypasses it. Sending a test event is supported only on an already deployed graph when explicitly requested.

## Component size

**Ajustes → Espacio** offers 50%, 75%, 100% and 125% sizes and **Distribuir en la mesa**. Voice accepts any scale between 0.5 and 1.25. The preference is saved on the headset and applies to new nodes, labels, colliders, ports and placement previews. Connections continue to follow scaled ports; their labels also shrink. Arranging packs four columns using the selected scale. Resizing and arranging do not change the AWS definition or invalidate its deployment. The architecture limit remains 12 resources.

## Validation

Play-mode tests cover component add/update/select/remove, directed connections and cycle rejection, undo, stale requests, manual draft protection, manual-only AWS confirmation, size persistence for new nodes, deployment preservation, and keeping the assistant enabled after hiding its panel. Voice state tests cover interruption, stale response rejection, capture continuity, privacy pause and explicit disconnection.

The opt-in live test sends two synthesized utterances through the actual PCM queue and native WebRTC in one session. It uses server speech detection and checks transcription, context tool calls and audio-response transcripts without a manual commit. It does not establish physical Quest microphone or acoustic echo performance; those require an actual spoken headset test. See current Validation and release reports for executed results.
