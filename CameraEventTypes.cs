namespace OpenCamInterop;

public static class CameraEventTypes
{
    public const string Prefix = "com.kalyterasystems.opencaminterop.";
    public const string ObjectDetected = Prefix + "object.detected.v1";
    public const string ObjectUpdated = Prefix + "object.updated.v1";
    public const string ObjectEnded = Prefix + "object.ended.v1";
    public const string SignalChanged = Prefix + "signal.changed.v1";
    public const string OnvifNotification = Prefix + "onvif.notification.v1";
}
