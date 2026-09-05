# Event contract

OpenCamInterop is an experimental, transport-free normalization layer for camera and NVR events. Callers supply bounded bytes plus delivery metadata; adapters return CloudEvents and stable diagnostics without opening a socket.

## Implemented inputs

| Input | CloudEvent type | Data schema |
| --- | --- | --- |
| Frigate `new` | `com.kalyterasystems.opencaminterop.object.detected.v1` | `urn:opencaminterop:schema:camera-object-event:1` |
| Frigate `update` | `com.kalyterasystems.opencaminterop.object.updated.v1` | `urn:opencaminterop:schema:camera-object-event:1` |
| Frigate `end` | `com.kalyterasystems.opencaminterop.object.ended.v1` | `urn:opencaminterop:schema:camera-object-event:1` |
| Canonical ONVIF motion `Changed` | `com.kalyterasystems.opencaminterop.signal.changed.v1` | `urn:opencaminterop:schema:camera-signal-event:1` |
| Other ONVIF notification | `com.kalyterasystems.opencaminterop.onvif.notification.v1` | `urn:opencaminterop:schema:onvif-notification-event:1` |

Every output is a CloudEvents 1.0 event with an absolute `source`, required UTC `time`, JSON data, and a versioned `type` and `dataschema`. The schemas in `schemas/v1` are consumer contracts; runtime decoding does not act as a general schema registry.

## Identity and time

Consumers must treat `(source, id)` as the uniqueness pair.

- A byte-for-byte Frigate redelivery on the same topic receives the same ID.
- An ONVIF ID hashes one normalized notification—topic, dialect, occurrence time, operation, and ordered items—so SOAP wrapper formatting and batch position do not change identity.
- Frigate occurrence time is `end_time` for `end`, otherwise `frame_time`, with `start_time` as a deterministic fallback.
- ONVIF requires a valid explicitly zoned `UtcTime`; malformed occurrence times are not silently replaced with delivery time.

Only a motion topic in the standard ONVIF topic namespace with a recognized Concrete or ConcreteSet dialect and the `Changed` property operation is promoted to `signal.changed.v1`. Synchronization operations such as `Initialized` and `Deleted` remain generic notifications.

## EventLab behavior

`inspect` adapts one local payload. `verify` loads the strict v1 fixture manifest, runs every registered case, evaluates its expected ordered event types or diagnostic, repeats successful adaptations to compare identities, and checks the generated matrix. `replay` preflights the same corpus and emits structured CloudEvents NDJSON in manifest order, optionally honoring relative delivery offsets.

Exit codes are stable within v1:

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 1 | Unexpected internal failure |
| 2 | Invalid command or input |
| 3 | Fixture expectation mismatch |
| 130 | Cancellation |

Machine-readable event output and generated Markdown use stdout. Human status, warnings, and errors use stderr.

## Non-goals

The v1 contract contains no MQTT client, ONVIF subscription client, camera discovery, dynamic plug-in loading, credential store, image handling, video handling, or network output. It is not a complete ontology, device certification, vendor support statement, or ONVIF conformance suite.

## Protocol references

- [CloudEvents specification and JSON format](https://github.com/cloudevents/spec)
- [Official CloudEvents C# SDK](https://github.com/cloudevents/sdk-csharp)
- [Frigate MQTT event documentation](https://docs.frigate.video/integrations/mqtt/)
- [ONVIF network interface specifications](https://www.onvif.org/profiles/specifications/)
- [OASIS WS-Topics 1.3](https://docs.oasis-open.org/wsn/wsn-ws_topics-1.3-spec-os.htm)
