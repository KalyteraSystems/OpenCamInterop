# Scrypted `ObjectsDetected` profile

OpenCamInterop's Scrypted adapter is a deliberately narrow, transport-free profile of the public `ObjectDetector` event payload. It normalizes an object **observation** only when the payload contains enough native information to do so deterministically. It does not infer a detection lifecycle.

## Evidence boundary

The profile was checked against Scrypted main commit [`d728c4ab7d62d698fdf0ab4a0428df964bb1b067`](https://github.com/koush/scrypted/commit/d728c4ab7d62d698fdf0ab4a0428df964bb1b067):

- [`EventListener` receives `eventSource`, `eventDetails`, and `eventData`, while `EventDetails` carries `eventId` and `eventTime`](https://github.com/koush/scrypted/blob/d728c4ab7d62d698fdf0ab4a0428df964bb1b067/sdk/types/src/types.input.ts#L83-L91).
- [`ObjectDetectionResult` and `ObjectsDetected`](https://github.com/koush/scrypted/blob/d728c4ab7d62d698fdf0ab4a0428df964bb1b067/sdk/types/src/types.input.ts#L1637-L1693) define optional tracked-object ids plus class, score, zones, recognition, geometry, history, media resources, payload timestamp, retention id, and generation source. The public comment explicitly says `sourceId` can identify either a camera or a plugin.
- [The MQTT mixin serializes only `eventData` and publishes it below the event-interface topic](https://github.com/koush/scrypted/blob/d728c4ab7d62d698fdf0ab4a0428df964bb1b067/plugins/mqtt/src/main.ts#L178-L194). `eventDetails.eventId` and `eventDetails.eventTime` therefore are not present in that JSON payload.
- [Reolink creates `ObjectsDetected` snapshots with `timestamp: Date.now()`](https://github.com/koush/scrypted/blob/d728c4ab7d62d698fdf0ab4a0428df964bb1b067/plugins/reolink/src/main.ts#L627-L651), can emit again while an AI state remains active, and [uses a separate motion-stop path rather than an `ObjectDetector` end event](https://github.com/koush/scrypted/blob/d728c4ab7d62d698fdf0ab4a0428df964bb1b067/plugins/reolink/src/main.ts#L680-L695).
- [Amcrest likewise emits a timestamped `ObjectsDetected` payload for each smart event](https://github.com/koush/scrypted/blob/d728c4ab7d62d698fdf0ab4a0428df964bb1b067/plugins/amcrest/src/main.ts#L294-L312).
- [Scrypted's Smart Motion Sensor consumes `ObjectDetector` observations](https://github.com/koush/scrypted/blob/d728c4ab7d62d698fdf0ab4a0428df964bb1b067/plugins/objectdetector/src/smart-motionsensor.ts#L176-L259) and derives motion lifetime with a timeout or a separate `MotionSensor` listener. That is further evidence that one `ObjectsDetected` delivery is not a portable new/update/end state transition.

These sources establish the supported shape and the observation semantics. They do not establish compatibility with a particular camera, plug-in, firmware version, or private Scrypted installation.

## Accepted profile

The caller supplies three pieces of trust-boundary metadata: a safe absolute CloudEvent `source`, an opaque camera id, and the exact input topic. The topic must be `ObjectDetector` or end in `/ObjectDetector`, matching Scrypted's event-interface publication shape; camera identity is never derived from the topic or from `ObjectsDetected.sourceId`.

The JSON payload must contain an integer `timestamp` interpreted as Unix milliseconds and representing 2000-01-01 or later. This explicit profile matches the current first-party emitters above, which use JavaScript `Date.now()`, and rejects second-based or otherwise ambiguous small timestamps instead of silently using delivery time.

Each emitted observation requires `detections[].id`, `className`, and a finite `score` from 0 through 1. `id` is optional in Scrypted's broad public type, so payloads without it are valid Scrypted data but are outside this deterministic profile. Current Reolink and Amcrest examples above omit object ids; OpenCamInterop rejects those deliveries rather than manufacturing correlation. Duplicate ids in one payload are also rejected as ambiguous.

One accepted detection becomes one `com.kalyterasystems.opencaminterop.object.observed.v1` CloudEvent. Its time comes only from the payload timestamp. Its id is a deterministic length-delimited hash over the normalized allowlist—adapter identity, caller camera id, native object id, timestamp, class, score, and ordered zones. Distinct objects in the same payload remain distinct, while excluded private fields do not alter or feed the public event id.

## Privacy allowlist

Normalized data retains only the adapter id, caller camera id, native object id, `className`, score as `confidence`, zones, and observation time. The adapter does not copy recognized `label`/`labelScore`, embeddings, bounding boxes, landmarks, clip paths, movement/history, media resources, input dimensions, `detectionId`, or `sourceId`.

The retained fields are not automatically anonymous. Object ids, class names, zones, camera ids, and caller `source` values can still be installation-specific or user-selected. Public fixtures therefore use conspicuously synthetic identifiers and generic classes. The adapter opens no Scrypted, MQTT, camera, or other network connection.
