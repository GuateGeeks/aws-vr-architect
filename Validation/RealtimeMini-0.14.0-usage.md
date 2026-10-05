# Live voice comparison

Synthetic speech through native WebRTC; one sample per mode, not a latency benchmark.

Model: gpt-realtime-2.1-mini. Cost estimates include received usage, not an invoice.

- Fast, English context: fixture end → commit 843 ms; commit → first audio 1043 ms; passed.
  Cost so far: USD ~0.0050 · esta ejecución; audio in/out=178/75; cached audio/text=64/2880; transcription tokens=113
- Natural, English context: fixture end → commit 4793 ms; commit → first audio 915 ms; passed.
  Cost so far: USD ~0.0094 · esta ejecución; audio in/out=546/161; cached audio/text=256/8832; transcription tokens=232
