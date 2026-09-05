# Fixture privacy and trust boundary

OpenCamInterop reduces accidental disclosure; it cannot prove that contributed data is anonymous. Fixture authors and reviewers remain responsible for the final sanitization decision.

## Never publish

Do not commit or attach:

- camera, NVR, broker, or account credentials;
- routable IP addresses, private hostnames, installation URLs, query tokens, or local paths;
- device serial numbers or other installation-specific identifiers;
- faces, people, real license plates, thumbnails, snapshots, audio, or video;
- a raw export copied from a private installation; or
- values merely hashed in place of being made synthetic.

Protocol namespace URIs required to reproduce parsing behavior are acceptable. Use conspicuously fake identifiers when a field must exist to exercise redaction or correlation, and explain that choice in the manifest note.

## Adapter behavior

- Payloads are limited to 1 MiB.
- Frigate output is built from an allowlist. Arbitrary native extensions, thumbnails, recognized plates, and sub-labels are not copied.
- Recognized ONVIF source and rule identifiers become stable opaque hashes.
- Generic ONVIF item names and ordering are retained, including duplicates, but values become `[redacted]`.
- XML DTDs and external entity resolution are prohibited; XML depth, notification count, and item count are bounded.
- Structured JSON rejects duplicate object members, oversized input, oversized batches, invalid typed v1 data, and mismatched v1 schemas.

Frigate camera IDs, object IDs, labels, and zones are retained because consumers need correlation. Caller-provided CloudEvent `source` values are emitted verbatim. Allowlisting and SHA-256 identifiers are not encryption or automatic anonymity, especially for low-entropy values.

## Review checklist

Before merging a fixture, reviewers should be able to answer yes to all of these:

1. The payload is synthetic or irreversibly reduced, not a raw export.
2. Every retained value is required to reproduce the distinct behavior.
3. No credential, address, serial, person, media, installation URL, or path remains.
4. The source is an opaque fixture URN.
5. The manifest note accurately describes the behavior and sanitization.
6. The executable expectation fails if the relevant behavior regresses.

When in doubt, do not publish the payload. Describe the parser shape privately in a security advisory or construct a new synthetic reproduction from scratch.
