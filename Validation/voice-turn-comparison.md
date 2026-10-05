# Live voice comparison

Synthetic speech through native WebRTC; one sample per mode, not a latency benchmark.

Model: gpt-realtime-2.1. Cost estimates include received usage, not an invoice.

- Fast, English context: fixture end → commit 850 ms; commit → first audio 671 ms; passed.
  Cost so far: USD ~0.0176 · esta ejecución; audio in/out=178/167; cached audio/text=128/5760; transcription tokens=113
- Natural, English context: fixture end → commit 4795 ms; commit → first audio 728 ms; passed.
  Cost so far: USD ~0.0400 · esta ejecución; audio in/out=546/348; cached audio/text=320/11712; transcription tokens=232
- Natural, Spanish correction: fixture end → commit 623 ms; commit → first audio 719 ms; passed.
  Cost so far: USD ~0.0893 · esta ejecución; audio in/out=1422/586; cached audio/text=768/17984; transcription tokens=372
