# Changelog

## Unreleased

No unreleased changes yet.

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
