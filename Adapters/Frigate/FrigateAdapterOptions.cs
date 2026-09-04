namespace OpenCamInterop.Adapters.Frigate;

public sealed class FrigateAdapterOptions
{
    public FrigateAdapterOptions(Uri source, string topicPrefix = "frigate")
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.IsAbsoluteUri)
            throw new ArgumentException("The Frigate source must be an absolute URI.", nameof(source));
        if (!string.IsNullOrEmpty(source.UserInfo))
            throw new ArgumentException("The Frigate source must not contain user information.", nameof(source));
        if (!string.IsNullOrEmpty(source.Query) || !string.IsNullOrEmpty(source.Fragment))
        {
            throw new ArgumentException(
                "The Frigate source must not contain a query string or fragment.",
                nameof(source));
        }
        if (string.IsNullOrWhiteSpace(topicPrefix))
            throw new ArgumentException("A Frigate MQTT topic prefix is required.", nameof(topicPrefix));
        if (!string.Equals(topicPrefix, topicPrefix.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("The Frigate MQTT topic prefix cannot contain surrounding whitespace.", nameof(topicPrefix));
        if (topicPrefix.EndsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("The Frigate MQTT topic prefix cannot end with a slash.", nameof(topicPrefix));
        if (topicPrefix.Contains("+", StringComparison.Ordinal) ||
            topicPrefix.Contains("#", StringComparison.Ordinal) ||
            topicPrefix.Contains("\0", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Frigate MQTT topic prefix cannot contain wildcards or null characters.",
                nameof(topicPrefix));
        }

        Source = source;
        TopicPrefix = topicPrefix;
    }

    public Uri Source { get; }

    public string TopicPrefix { get; }

    internal string EventsTopic => $"{TopicPrefix}/events";
}
