# Security Policy

## Report a vulnerability privately

Do not disclose suspected vulnerabilities in public issues, discussions, fixtures, or pull requests.

Use **Report a vulnerability** on this repository's Security tab. Include the affected version or commit, bounded reproduction steps, impact, and a suggested mitigation when available. Maintainers will coordinate disclosure after a fix is ready.

Do not include real camera credentials, private payloads, addresses, images, video, or other personal data in a report unless a maintainer explicitly establishes a suitable private transfer method.

## Supported versions

OpenCamInterop is an alpha. Security fixes apply to the latest tagged alpha and the default branch; older commits and unofficial builds are not supported.

## Trust boundary

The library and EventLab CLI parse untrusted caller-supplied local bytes but deliberately own no transport. They do not connect to cameras, MQTT brokers, ONVIF endpoints, discovery services, or arbitrary URLs.

Payload, manifest, corpus, event-count, structured JSON, replay-duration, and output sizes are bounded. XML DTD processing and external resolution are prohibited. Fixture paths cannot escape the corpus or traverse symbolic links. These controls reduce risk but do not make private device exports safe to publish.
