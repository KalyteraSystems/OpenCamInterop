namespace OpenCamInterop.Adapters.Scrypted;

public sealed class ScryptedAdapterOptions
{
    private const int MaxCameraIdLength = 512;
    private const int MaxTopicLength = 256;

    public ScryptedAdapterOptions(
        Uri source,
        string cameraId,
        string eventTopic = "ObjectDetector")
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.IsAbsoluteUri)
            throw new ArgumentException("The Scrypted source must be an absolute URI.", nameof(source));
        if (!string.IsNullOrEmpty(source.UserInfo))
            throw new ArgumentException("The Scrypted source must not contain user information.", nameof(source));
        if (!string.IsNullOrEmpty(source.Query) || !string.IsNullOrEmpty(source.Fragment))
        {
            throw new ArgumentException(
                "The Scrypted source must not contain a query string or fragment.",
                nameof(source));
        }

        if (string.IsNullOrWhiteSpace(cameraId) ||
            cameraId.Length > MaxCameraIdLength ||
            cameraId.Any(char.IsControl) ||
            !string.Equals(cameraId, cameraId.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Scrypted camera id must be a bounded opaque identifier without surrounding whitespace.",
                nameof(cameraId));
        }

        if (string.IsNullOrWhiteSpace(eventTopic) ||
            eventTopic.Length > MaxTopicLength ||
            eventTopic.Any(char.IsControl) ||
            !string.Equals(eventTopic, eventTopic.Trim(), StringComparison.Ordinal) ||
            eventTopic.Contains('+', StringComparison.Ordinal) ||
            eventTopic.Contains('#', StringComparison.Ordinal) ||
            !(string.Equals(eventTopic, "ObjectDetector", StringComparison.Ordinal) ||
              eventTopic.EndsWith("/ObjectDetector", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "The Scrypted event topic must be ObjectDetector or a bounded MQTT topic ending in /ObjectDetector without wildcards.",
                nameof(eventTopic));
        }

        Source = source;
        CameraId = cameraId;
        EventTopic = eventTopic;
    }

    public Uri Source { get; }

    public string CameraId { get; }

    public string EventTopic { get; }
}
