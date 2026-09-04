# OpenCamInterop fixture corpus v1

These synthetic payloads exercise the public v1 adapter contract without requiring camera hardware. Current fixtures are referenced by the adapter tests; every new fixture must add its own executable assertion so it cannot become an unverified compatibility claim.

Fixtures must be synthetic or irreversibly sanitized. Never include credentials, routable addresses, device serial numbers, faces, license plates, snapshots, thumbnails, local paths, or an export copied directly from a private installation.

To contribute a newly observed interoperability behavior:

1. Reduce it to the smallest synthetic payload that still reproduces the behavior.
2. Add the payload beneath the adapter directory.
3. Add an assertion to `IPCamLapse.Tests/OpenCamInteropAdapterTests.cs` describing the expected event or diagnostic.
4. State in the pull request which behavior is new; a device-list row by itself is not a compatibility test.

The corpus is not an ONVIF conformance suite and does not certify any camera, NVR, or vendor.
