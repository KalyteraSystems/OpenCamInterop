# OpenCamInterop EventLab CLI

EventLab is an offline .NET 10 command-line tool for inspecting sanitized Frigate, Scrypted `ObjectsDetected`, and ONVIF payloads, verifying the versioned fixture corpus, and replaying it deterministically. It opens no network connections and has no transport clients.

From the standalone repository root, prepare the Release build:

```text
dotnet restore OpenCamInterop.sln --locked-mode
dotnet build OpenCamInterop.sln --configuration Release --no-restore
```

Then run commands without mixing build or restore output into stdout. These single-line examples work in PowerShell and Bash; rebuild after source changes:

```text
dotnet run --project tools/OpenCamInterop.Tool --configuration Release --no-build --no-restore -- inspect --adapter frigate --input fixtures/v1/frigate/object-new.json --source urn:opencaminterop:local:inspect
dotnet run --project tools/OpenCamInterop.Tool --configuration Release --no-build --no-restore -- inspect --adapter scrypted --input fixtures/v1/scrypted/objects-detected.json --source urn:opencaminterop:local:scrypted --camera-id synthetic-camera-alpha --channel fixture/camera/ObjectDetector
dotnet run --project tools/OpenCamInterop.Tool --configuration Release --no-build --no-restore -- verify --manifest fixtures/v1/manifest.json
dotnet run --project tools/OpenCamInterop.Tool --configuration Release --no-build --no-restore -- replay --manifest fixtures/v1/manifest.json --no-wait
dotnet run --project tools/OpenCamInterop.Tool --configuration Release --no-build --no-restore -- --help
```

For self-contained release bundles, use `.\opencaminterop.exe` on Windows or `./opencaminterop` on Linux in place of the `dotnet run ... --` prefix. See the [release quick start](../../README.md#try-a-release-bundle-without-installing-net) for extraction and verification.

`inspect` writes one structured CloudEvent JSON object to stdout, or a CloudEvents batch when a single payload produces multiple events. Adapter warnings and errors use stable lines on stderr. `replay` preflights the corpus sequence, then writes one structured CloudEvent JSON object per line at the relative `receivedAt` offsets; `--no-wait` emits the same NDJSON immediately in manifest order. The current checked-in manifest is a corpus inventory with equal timestamps, not a claim that its unrelated cases form one real-world trace.

`verify` strictly validates the manifest and corpus, checks expected event types or diagnostic codes, and reruns successful cases to compare their CloudEvent `(source,id)` pairs. It also checks that `COMPATIBILITY.md` is the deterministic projection of the manifest. To reproduce that projection without modifying files:

```text
dotnet run --project tools/OpenCamInterop.Tool --configuration Release --no-build --no-restore -- verify --manifest fixtures/v1/manifest.json --print-matrix
```

The `--print-matrix` form verifies cases and expectations without checking the previous matrix for freshness. Review stdout before replacing the matrix, save it as UTF-8 without a byte-order mark and with LF line endings, then run verification without `--print-matrix` to check the saved file. Human status remains on stderr, so stdout contains only the generated Markdown.

`expectedEventTypes` is an ordered sequence of zero through 256 recognized types, including repeats. Both count and order must match the emitted events. Use `[]` for a successful observation with no events; verification reports `no events` in the matrix and replay writes no event lines for that case. Event and diagnostic expectations remain mutually exclusive.

`inspect` requires at least one output event. For a valid empty Scrypted observation it writes no stdout, reports `error [adapter.no-events]` on stderr, and exits with code `2`. Use a manifest case with `"expectedEventTypes": []` to verify or replay this successful adapter outcome. See the [fixture guide](../../fixtures/v1/README.md) and [Scrypted profile](../../docs/SCRYPTED.md).

Payloads are capped at 1 MiB, manifests at 64 KiB and 256 cases, replay output at 4 MiB, and waited replay at ten minutes. Manifest payload paths must remain beneath the manifest directory, use the matching adapter directory and extension, and contain no symbolic links. Manifest sources must be opaque absolute URNs. Scrypted `inspect` requires `--camera-id`; `--channel` can carry the configured MQTT path ending in `/ObjectDetector` and is never used as camera identity.

Exit codes are stable: `0` success, `1` unexpected internal failure, `2` invalid CLI or input, `3` fixture expectation failure, and `130` cancellation. Use `--help` for the complete option summary.
