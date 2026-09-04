namespace OpenCamInterop.Adapters;

public sealed record AdapterMessage(
    string Channel,
    ReadOnlyMemory<byte> Payload,
    string ContentType,
    DateTimeOffset ReceivedAt,
    IReadOnlyDictionary<string, string>? Metadata = null);
