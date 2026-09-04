# OpenCamInterop

OpenCamInterop is an alpha-stage .NET 8 library for turning bounded, offline camera-event payloads into validated [CloudEvents 1.0](https://cloudevents.io/) JSON. The first adapters cover Frigate object lifecycle messages and ONVIF notifications; the library opens no sockets and stores no credentials.

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

Inputs are capped at 1 MiB. Structured CloudEvents JSON is capped at 4 MiB and batches at 256 events. Frigate output uses an explicit field allowlist, but retained camera, object, label, and zone identifiers are not automatically anonymous. Generic ONVIF item values are always replaced with `[redacted]`; recognized source identifiers are represented only by stable opaque hashes. Caller-provided `source` values are emitted verbatim, and hashes support correlation rather than anonymity for low-entropy identifiers.

The package includes the v1 JSON Schemas. See the repository's [OpenCamInterop design and contribution guide](https://github.com/KalyteraSystems/IPCamLapse/blob/main/docs/OPEN_CAM_INTEROP.md) for the complete contract, privacy model, and fixture workflow.
