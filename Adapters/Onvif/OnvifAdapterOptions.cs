namespace OpenCamInterop.Adapters.Onvif;

public sealed record OnvifAdapterOptions
{
    public OnvifAdapterOptions(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsAbsoluteUri ||
            !string.IsNullOrEmpty(source.UserInfo) ||
            !string.IsNullOrEmpty(source.Query) ||
            !string.IsNullOrEmpty(source.Fragment))
        {
            throw new ArgumentException(
                "The adapter source must be an absolute URI without user information, query, or fragment.",
                nameof(source));
        }

        Source = source;
    }

    public Uri Source { get; }
}
