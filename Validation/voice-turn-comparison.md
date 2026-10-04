# Live voice comparison

Synthetic speech through native WebRTC; one sample per mode, not a latency benchmark.

- Fast, English context: fixture end → commit 851 ms; commit → first audio 950 ms; passed.
- Natural, English context: fixture end → commit 4799 ms; commit → first audio 649 ms; passed.
- Natural, Spanish correction: fixture end → commit 542 ms; commit → first audio 783 ms; passed.
