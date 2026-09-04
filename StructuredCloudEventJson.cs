using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;

namespace OpenCamInterop;

public static class StructuredCloudEventJson
{
    public const string StructuredContentType = "application/cloudevents+json";
    public const string BatchContentType = "application/cloudevents-batch+json";
    public const int MaxEncodedBytes = 4 * 1024 * 1024;
    public const int MaxBatchEvents = 256;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    public static ReadOnlyMemory<byte> Serialize(CloudEvent cloudEvent)
    {
        InteropCloudEventValidator.EnsureValid(cloudEvent);
        var encoded = CreateFormatter().EncodeStructuredModeMessage(cloudEvent, out _);
        EnsureEncodedSize(encoded);
        return encoded;
    }

    public static ReadOnlyMemory<byte> SerializeBatch(IEnumerable<CloudEvent> cloudEvents)
    {
        ArgumentNullException.ThrowIfNull(cloudEvents);
        var materialized = cloudEvents.Take(MaxBatchEvents + 1).ToArray();
        if (materialized.Length > MaxBatchEvents)
            throw new ArgumentOutOfRangeException(nameof(cloudEvents), $"A batch can contain at most {MaxBatchEvents} events.");
        foreach (var cloudEvent in materialized)
            InteropCloudEventValidator.EnsureValid(cloudEvent);

        var encoded = CreateFormatter().EncodeBatchModeMessage(materialized, out _);
        EnsureEncodedSize(encoded);
        return encoded;
    }

    public static CloudEvent Deserialize(ReadOnlyMemory<byte> utf8Json)
    {
        ValidateEnvelopeJson(utf8Json, expectBatch: false);
        var cloudEvent = CreateFormatter().DecodeStructuredModeMessage(
            utf8Json,
            new ContentType(StructuredContentType),
            extensionAttributes: null);
        InteropCloudEventValidator.EnsureValid(cloudEvent);
        return cloudEvent;
    }

    public static IReadOnlyList<CloudEvent> DeserializeBatch(ReadOnlyMemory<byte> utf8Json)
    {
        ValidateEnvelopeJson(utf8Json, expectBatch: true);
        var cloudEvents = CreateFormatter().DecodeBatchModeMessage(
            utf8Json,
            new ContentType(BatchContentType),
            extensionAttributes: null);
        foreach (var cloudEvent in cloudEvents)
            InteropCloudEventValidator.EnsureValid(cloudEvent);
        return cloudEvents;
    }

    private static JsonEventFormatter CreateFormatter()
    {
        return new JsonEventFormatter(SerializerOptions, DocumentOptions);
    }

    private static void EnsureEncodedSize(ReadOnlyMemory<byte> encoded)
    {
        if (encoded.Length > MaxEncodedBytes)
            throw new InvalidOperationException($"Encoded CloudEvents JSON exceeds the {MaxEncodedBytes}-byte limit.");
    }

    private static void ValidateEnvelopeJson(ReadOnlyMemory<byte> utf8Json, bool expectBatch)
    {
        if (utf8Json.IsEmpty || utf8Json.Length > MaxEncodedBytes)
            throw new JsonException($"CloudEvents JSON must contain from 1 through {MaxEncodedBytes} bytes.");

        using var document = JsonDocument.Parse(utf8Json, DocumentOptions);
        if (expectBatch)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("A CloudEvents batch must be a JSON array.");
            if (document.RootElement.GetArrayLength() > MaxBatchEvents)
                throw new JsonException($"A CloudEvents batch can contain at most {MaxBatchEvents} events.");

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Every CloudEvents batch entry must be a JSON object.");
                EnsureNoDuplicateProperties(item);
            }
        }
        else
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("A structured CloudEvent must be a JSON object.");
            EnsureNoDuplicateProperties(document.RootElement);
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException("CloudEvents JSON contains a duplicate object property.");
                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                EnsureNoDuplicateProperties(item);
        }
    }
}
