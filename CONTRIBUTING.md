# Contributing to OpenCamInterop

OpenCamInterop values distinct, reproducible interoperability behavior over activity volume. A useful pull request turns one uncovered behavior into a sanitized payload and an executable expectation.

## Before you start

- Search existing issues and fixtures before opening new work.
- Open an issue before changing a public contract or manifest version.
- Report suspected vulnerabilities privately through GitHub Security Advisories as described in [SECURITY.md](SECURITY.md).
- Never attach a raw device export, image, video, credential, URL, address, serial number, person, face, plate, or local path.
- Keep one pull request focused on one behavior.

## Local setup

Install the .NET 10 SDK, then run:

```console
git clone https://github.com/KalyteraSystems/OpenCamInterop.git
cd OpenCamInterop
dotnet restore OpenCamInterop.sln --locked-mode
dotnet build OpenCamInterop.sln --configuration Release --no-restore
dotnet test OpenCamInterop.sln --configuration Release --no-build
dotnet format OpenCamInterop.sln --verify-no-changes --no-restore
dotnet list OpenCamInterop.sln package --vulnerable --include-transitive
```

No camera, broker, network service, media file, or FFmpeg installation is needed.

## Add a fixture behavior

1. Reduce the behavior to the smallest synthetic payload that still reproduces it.
2. Put the payload under `fixtures/v1/frigate` or `fixtures/v1/onvif`.
3. Add one stable case to `fixtures/v1/manifest.json` with its replay inputs, expected event types or diagnostic, and an honest sanitization note.
4. Run EventLab verification.
5. Regenerate the matrix to stdout and review it before replacing the checked-in file:

```console
dotnet run --project tools/OpenCamInterop.Tool -- verify --manifest fixtures/v1/manifest.json --print-matrix
```

6. Explain the distinct input behavior, expected normalization, and information removed during sanitization in the pull request.

The manifest schema is available at `schemas/v1/fixture-manifest.schema.json`. CI rejects missing or orphaned payloads, duplicate IDs and properties, unsupported adapters, unsafe paths and links, oversized inputs, nondeterministic identities, expectation drift, and stale generated matrices.

## Pull requests

- Add or update tests for behavior changes.
- Keep stdout machine-readable and stderr diagnostic-only in EventLab commands.
- Preserve offline operation and caller-owned transports.
- Update documentation when public behavior, limits, or privacy assumptions change.
- Confirm Windows and Ubuntu CI are green.
- Link a fully resolved issue with `Fixes #123`.

First-time contributors may need maintainer approval before GitHub Actions runs. Maintainers aim to acknowledge a contribution within two business days and review a ready pull request within three business days.

By contributing, you agree that your contribution is licensed under the Apache License 2.0.
