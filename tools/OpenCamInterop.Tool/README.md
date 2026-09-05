# OpenCamInterop EventLab CLI

EventLab is an offline .NET 10 command-line tool for inspecting sanitized Frigate and ONVIF payloads, verifying the versioned fixture corpus, and replaying it deterministically. It opens no network connections and has no transport clients.

Run it from the standalone repository root:

```text
dotnet run --project tools/OpenCamInterop.Tool -- inspect --adapter frigate --input fixtures/v1/frigate/object-new.json --source urn:opencaminterop:local:inspect
dotnet run --project tools/OpenCamInterop.Tool -- verify --manifest fixtures/v1/manifest.json
dotnet run --project tools/OpenCamInterop.Tool -- replay --manifest fixtures/v1/manifest.json --no-wait
```

`inspect` writes one structured CloudEvent JSON object to stdout, or a CloudEvents batch when a single ONVIF payload contains multiple notifications. Adapter warnings and errors use stable lines on stderr. `replay` preflights the corpus sequence, then writes one structured CloudEvent JSON object per line at the relative `receivedAt` offsets; `--no-wait` emits the same NDJSON immediately in manifest order. The current checked-in manifest is a corpus inventory with equal timestamps, not a claim that its unrelated cases form one real-world trace.

`verify` strictly validates the manifest and corpus, checks expected event types or diagnostic codes, and reruns successful cases to compare their CloudEvent `(source,id)` pairs. It also checks that `COMPATIBILITY.md` is the deterministic projection of the manifest. To reproduce that projection without modifying files:

```text
dotnet run --project tools/OpenCamInterop.Tool -- verify --manifest fixtures/v1/manifest.json --print-matrix
```

Redirect stdout to replace the matrix after reviewing a manifest change. Human status remains on stderr, so stdout contains only the generated Markdown.

Payloads are capped at 1 MiB, manifests at 64 KiB and 256 cases, replay output at 4 MiB, and waited replay at ten minutes. Manifest payload paths must remain beneath the manifest directory, use the matching adapter directory and extension, and contain no symbolic links. Manifest sources must be opaque absolute URNs.

Exit codes are stable: `0` success, `1` unexpected internal failure, `2` invalid CLI or input, `3` fixture expectation failure, and `130` cancellation. Use `--help` for the complete option summary.
