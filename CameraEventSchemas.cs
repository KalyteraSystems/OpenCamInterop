namespace OpenCamInterop;

public static class CameraEventSchemas
{
    public static Uri CameraObjectV1 { get; } = new(
        "urn:opencaminterop:schema:camera-object-event:1",
        UriKind.Absolute);

    public static Uri CameraSignalV1 { get; } = new(
        "urn:opencaminterop:schema:camera-signal-event:1",
        UriKind.Absolute);

    public static Uri OnvifNotificationV1 { get; } = new(
        "urn:opencaminterop:schema:onvif-notification-event:1",
        UriKind.Absolute);
}
