# Public evidence

The raw JSON recordings and exact measurement source are retained byte-for-byte. Session and artifact summaries are allowlisted excerpts; machine partition IDs, disk serials, private file paths, recovery data and ROM/profile payloads are omitted.

These are first-party records from one PC. `tools/verify_results.py` checks internal consistency and recomputes the comparison, not independent provenance or current GPU performance.

- `raw/`: original baseline, final internal automatic after-run and mixed-load record.
- `measurement-source/`: unchanged original measurement scripts, with their original local dependency/path assumptions.
- `verified-session.json`: sanitized hardware/boot/result summary.
- `reference-artifacts.json`: hashes of tested artifacts, not distributed binaries.
- `host-test-history.json`: historical simulated checks before hardware execution.
- `source-provenance.json`: upstream base, original project and copied source hashes.
- `health-example.json`: sanitized excerpt from a real check of the public diagnostics tool; no live-performance test.
- `package-validation.json`: local packaging checks; separate from GPU qualification.

Full narrative: [RESULTS.md](../RESULTS.md).
