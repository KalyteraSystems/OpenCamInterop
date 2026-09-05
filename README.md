# OpenCamInterop

[![CI](https://github.com/KalyteraSystems/OpenCamInterop/actions/workflows/ci.yml/badge.svg)](https://github.com/KalyteraSystems/OpenCamInterop/actions/workflows/ci.yml)
[![CodeQL](https://github.com/KalyteraSystems/OpenCamInterop/actions/workflows/codeql.yml/badge.svg)](https://github.com/KalyteraSystems/OpenCamInterop/actions/workflows/codeql.yml)

OpenCamInterop turns sanitized Frigate and ONVIF event quirks into deterministic, offline compatibility tests. Its EventLab CLI can inspect one native payload, verify every case in a versioned fixture corpus, and replay normalized [CloudEvents 1.0](https://cloudevents.io/) as streaming NDJSON.

The project opens no network connections, discovers no devices, stores no credentials, and processes no camera images or video. Contributors can reproduce an interoperability behavior without owning the original hardware or sharing a raw private capture.

## Try the executable corpus

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```console
git clone https://github.com/KalyteraSystems/OpenCamInterop.git
cd OpenCamInterop
dotnet restore OpenCamInterop.sln --locked-mode
dotnet run --project tools/OpenCamInterop.Tool -- verify --manifest fixtures/v1/manifest.json
dotnet run --project tools/OpenCamInterop.Tool -- replay --manifest fixtures/v1/manifest.json --no-wait
```

`verify` checks strict manifest parsing, complete fixture registration, expected event types or diagnostics, deterministic `(source, id)` pairs, and the generated [compatibility matrix](fixtures/v1/COMPATIBILITY.md). `replay` preflights the corpus and emits one structured CloudEvent per stdout line; omit `--no-wait` to honor relative `receivedAt` offsets. Human diagnostics stay on stderr.

Inspect one payload without adding it to the corpus:

```console
dotnet run --project tools/OpenCamInterop.Tool -- inspect \
  --adapter frigate \
  --input fixtures/v1/frigate/object-new.json \
  --source urn:opencaminterop:local:inspect
```

Use `--help` for the complete command and exit-code contract. The [EventLab guide](tools/OpenCamInterop.Tool/README.md) documents output and safety limits.

## Contribute one real behavior

The most valuable contribution is one previously uncovered behavior reduced to a small synthetic payload and an executable expectation. Namespace variation, reconnect duplication, unusual ordering, missing fields, property operations, and bounded vendor extensions are all useful when they produce a distinct tested result.

Never submit a raw device export. Remove credentials, routable addresses, serial numbers, people, faces, plates, snapshots, thumbnails, installation URLs, and local paths before opening a pull request. Start with the [fixture guide](fixtures/v1/README.md) and [contribution guide](CONTRIBUTING.md).

A model name or compatibility-table row is not evidence by itself. The generated matrix reports fixture behavior only; it is not device certification, vendor support, or ONVIF conformance.

## Library API

The transport-free .NET 10 library accepts caller-owned bytes and returns validated CloudEvents:

```csharp
using System.Text;
using OpenCamInterop;
using OpenCamInterop.Adapters;
using OpenCamInterop.Adapters.Frigate;

var adapter = new FrigateEventAdapter(new Uri("urn:camera:lab-nvr"));
var result = adapter.Adapt(new AdapterMessage(
    "frigate/events",
    Encoding.UTF8.GetBytes(json),
    "application/json",
    DateTimeOffset.UtcNow));

if (result.IsSuccess)
{
    ReadOnlyMemory<byte> batch = StructuredCloudEventJson.SerializeBatch(result.Events);
}
```

Inputs are capped at 1 MiB. Structured CloudEvents JSON is capped at 4 MiB and batches at 256 events. Frigate output uses an explicit field allowlist, while generic ONVIF values are redacted. Retained identifiers and caller-provided `source` values are not automatically anonymous; use opaque URNs and synthetic identifiers in shared fixtures. See the [event contract](docs/CONTRACT.md) and [privacy boundary](docs/PRIVACY.md).

## Honest alpha status

The checked-in baseline is intentionally small:

| Measure | Current evidence |
| --- | ---: |
| Executable cases | 4 synthetic cases |
| Payloads | 3 synthetic payloads |
| Input adapters | 2 (Frigate and ONVIF) |
| Externally derived behavior cases | 0 |
| Independent downstream consumers | 0 |
| Non-maintainer behavior contributions | 0 |
| First-party consumers | 1 (IPCamLapse) |

The project does not count stars, generated matrix rows, mechanically split fixtures, or maintainer work as external adoption. The north-star measure is a distinct externally reported behavior merged as a sanitized fixture plus an executable expected result. See [project status](docs/PROJECT_STATUS.md).

## Non-goals

OpenCamInterop is not an NVR, camera viewer, MQTT bridge, ONVIF client, discovery tool, conformance suite, or certification program. Frigate and ONVIF names identify input formats and do not imply endorsement.

The API and fixture manifest are pre-1.0 and may change with documented migration notes. No NuGet package has been published; source and GitHub release archives are the supported alpha delivery paths.

## Development

```console
dotnet restore OpenCamInterop.sln --locked-mode
dotnet build OpenCamInterop.sln --configuration Release --no-restore
dotnet test OpenCamInterop.sln --configuration Release --no-build
dotnet format OpenCamInterop.sln --verify-no-changes --no-restore
dotnet list OpenCamInterop.sln package --vulnerable --include-transitive
```

CI runs on Windows and Ubuntu. Security issues should be reported privately as described in [SECURITY.md](SECURITY.md). The [project history](docs/HISTORY.md) records the verified extraction point and the original IPCamLapse pull requests.
