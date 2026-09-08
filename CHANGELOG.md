# Changelog

## Unreleased

## 0.1.0-alpha.3 - 2026-09-08

### Fixed

- EventLab fixture expectations now support ordered repeated event types and successful zero-event outputs, while retaining the 256-event bound and mutually exclusive diagnostic expectations
- Frigate and Scrypted adapters return stable invalid-JSON diagnostics when decoded fields or property names contain malformed Unicode

## 0.1.0-alpha.2 - 2026-09-07

### Added

- A structured issue form for proposing documented camera-event input families beyond Frigate and ONVIF
- A transport-free, observation-only Scrypted `ObjectsDetected` adapter with explicit camera identity, deterministic tracked-object correlation, a privacy allowlist, and synthetic EventLab coverage

### Changed

- The README now documents a tested, no-SDK path for verifying and replaying the self-contained release corpus
- Scrypted object events are modeled as `object.observed.v1`; the adapter deliberately refuses to invent new/update/end lifecycle semantics or object ids

## 0.1.0-alpha.1 - 2026-09-05

### Added

- Transport-free .NET 10 contracts and deterministic CloudEvents serialization
- Bounded Frigate object-lifecycle and ONVIF notification adapters
- Offline EventLab `inspect`, `verify`, and streaming `replay` commands
- Versioned synthetic fixture manifest and generated compatibility matrix
- Windows and Ubuntu tests for adapter, privacy, parsing, path, timing, and output behavior

### Security

- Payload, manifest, batch, path traversal, XML entity, corpus size, and replay duration limits
- Allowlisted Frigate output and redacted generic ONVIF item values

All checked-in fixtures are synthetic. This release makes no physical-device, vendor-compatibility, adoption, certification, or ONVIF-conformance claim.
