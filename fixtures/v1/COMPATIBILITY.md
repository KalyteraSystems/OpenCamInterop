<!-- Generated from manifest.json. Do not edit by hand. -->
# OpenCamInterop executable fixture matrix v1

This matrix records adapter behavior proven by synthetic, offline fixtures. It does not claim device, NVR, firmware, or vendor compatibility and is not an ONVIF conformance statement.

| Case | Adapter | Payload | Expected result | Sanitized behavior note |
| --- | --- | --- | --- | --- |
| `frigate.object-new.detected` | `frigate` | [`frigate/object-new.json`](frigate/object-new.json) | event `com.kalyterasystems.opencaminterop.object.detected.v1` | Synthetic object lifecycle shape with deliberately fake sentinels; private native fields are omitted from normalized output. |
| `frigate.object-new.unsupported-channel` | `frigate` | [`frigate/object-new.json`](frigate/object-new.json) | diagnostic `frigate.channel.unsupported` | Reuses the synthetic payload to verify channel rejection before parsing; all identifiers remain fake sentinels. |
| `onvif.cell-motion-changed` | `onvif` | [`onvif/cell-motion-changed.xml`](onvif/cell-motion-changed.xml) | event `com.kalyterasystems.opencaminterop.signal.changed.v1` | Synthetic standard-topic motion change; source identifiers are synthetic and normalized to opaque hashes. |
| `onvif.pullpoint-region-motion` | `onvif` | [`onvif/pullpoint-region-motion.xml`](onvif/pullpoint-region-motion.xml) | event `com.kalyterasystems.opencaminterop.signal.changed.v1` | Synthetic PullPoint region motion; source and rule identifiers are synthetic and normalized to opaque hashes. |
