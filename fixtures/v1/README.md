# OpenCamInterop fixture corpus v1

These synthetic payloads exercise the public v1 adapter contract without requiring camera hardware. The versioned [`manifest.json`](manifest.json) is the executable inventory: the offline manifest tests replay every case through its named adapter and verify its ordered event types or stable error diagnostic. Successful cases are replayed twice to prove that their event IDs are deterministic.

The manifest is intentionally strict and bounded. It allows at most 256 cases and 64 KiB of manifest JSON, limits payloads to the adapters' 1 MiB ceiling, accepts only the `frigate` and `onvif` adapters, and requires relative canonical paths that stay below this directory without symbolic links. Every JSON or XML payload in the corpus must be represented by at least one case. `expectedEventTypes` and `expectedDiagnosticCode` are mutually exclusive; event type order is significant. Case order is also replay order, so `receivedAt` values must be nondecreasing UTC timestamps.

[`COMPATIBILITY.md`](COMPATIBILITY.md) is generated deterministically from the manifest and checked in CI. Do not edit it by hand. It reports executable fixture behavior only; it is not a list of supported devices or vendors.

Fixtures must be synthetic or irreversibly sanitized. Never include credentials, routable addresses, device serial numbers, faces, license plates, snapshots, thumbnails, local paths, or an export copied directly from a private installation. Never include a private installation URL; required protocol namespace URIs are acceptable. When a native field must exist solely to prove redaction, use a conspicuously fake sentinel that cannot be mistaken for captured data, and explain that choice in the case note.

To contribute a newly observed interoperability behavior:

1. Reduce it to the smallest synthetic payload that still reproduces the behavior.
2. Add the payload beneath its adapter directory.
3. Register a stable case ID, replay inputs, one expected outcome, and a short sanitization note in `manifest.json`.
4. Regenerate `COMPATIBILITY.md` with the same deterministic format enforced by `OpenCamInteropFixtureManifestTests`.
5. State in the pull request which behavior is new; a device-list row by itself is not a compatibility test.

The corpus is not an ONVIF conformance suite and does not certify any camera, NVR, vendor, firmware, or deployment.
