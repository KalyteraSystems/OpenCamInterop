using System.Globalization;
using System.Text.Json;
using CloudNative.CloudEvents;

namespace OpenCamInterop.Adapters.Scrypted;

public sealed class ScryptedObjectsDetectedAdapter : ICameraEventAdapter
{
    public const string AdapterId = "scrypted.objects-detected.v1";

    private const int MaxIdentifierLength = 512;
    private const int MaxDetectionCount = 256;
    private const int MaxZoneCount = 100;
    private const int MaxJsonNodes = 8192;
    private static readonly long MinimumUnixMilliseconds =
        new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };
    private readonly ScryptedAdapterOptions _options;

    public ScryptedObjectsDetectedAdapter(ScryptedAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public string Id => AdapterId;

    public AdapterResult Adapt(AdapterMessage message)
    {
        if (message is null)
            return AdapterResult.Failure("message.required", "An adapter message is required.");

        if (!string.Equals(message.Channel, _options.EventTopic, StringComparison.Ordinal))
        {
            return AdapterResult.Failure(
                "scrypted.channel.unsupported",
                "The adapter accepts only its configured Scrypted ObjectDetector topic.",
                nameof(message.Channel));
        }

        if (!IsJsonContentType(message.ContentType))
        {
            return AdapterResult.Failure(
                "scrypted.content-type.unsupported",
                "The Scrypted ObjectsDetected payload must use the application/json content type.",
                nameof(message.ContentType));
        }

        var payloadDiagnostic = AdapterPayloadGuard.Validate(message);
        if (payloadDiagnostic is not null)
            return Failure(payloadDiagnostic);

        try
        {
            using var document = JsonDocument.Parse(message.Payload, DocumentOptions);
            var structureDiagnostic = ValidateJsonStructure(document.RootElement);
            return structureDiagnostic is null
                ? AdaptDocument(document.RootElement)
                : Failure(structureDiagnostic);
        }
        catch (JsonException)
        {
            return AdapterResult.Failure(
                "scrypted.json.invalid",
                "The Scrypted ObjectsDetected payload is not valid JSON.",
                "$");
        }
    }

    private AdapterResult AdaptDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return InvalidField("The Scrypted ObjectsDetected payload must be a JSON object.", "$");

        var timestampResult = ReadRequiredUnixMilliseconds(root, "timestamp", "$.timestamp");
        if (timestampResult.Diagnostic is not null)
            return Failure(timestampResult.Diagnostic);

        if (!root.TryGetProperty("detections", out var detections) || detections.ValueKind == JsonValueKind.Null)
            return AdapterResult.Success();
        if (detections.ValueKind != JsonValueKind.Array)
            return InvalidField("The detections field must be an array.", "$.detections");
        if (detections.GetArrayLength() > MaxDetectionCount)
            return InvalidField($"The detections field can contain at most {MaxDetectionCount} entries.", "$.detections");

        var parsed = new List<ParsedDetection>(detections.GetArrayLength());
        var objectIds = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var detection in detections.EnumerateArray())
        {
            var path = $"$.detections[{index}]";
            if (detection.ValueKind != JsonValueKind.Object)
                return InvalidField("Every Scrypted detection must be a JSON object.", path);

            var objectIdResult = ReadRequiredString(detection, "id", $"{path}.id");
            if (objectIdResult.Diagnostic is not null)
                return Failure(objectIdResult.Diagnostic);
            if (!objectIds.Add(objectIdResult.Value!))
            {
                return InvalidField(
                    "Detection ids must be unique within one ObjectsDetected payload.",
                    $"{path}.id");
            }

            var classNameResult = ReadRequiredString(detection, "className", $"{path}.className");
            if (classNameResult.Diagnostic is not null)
                return Failure(classNameResult.Diagnostic);

            var scoreResult = ReadRequiredConfidence(detection, "score", $"{path}.score");
            if (scoreResult.Diagnostic is not null)
                return Failure(scoreResult.Diagnostic);

            var zonesResult = ReadStringArray(detection, "zones", $"{path}.zones");
            if (zonesResult.Diagnostic is not null)
                return Failure(zonesResult.Diagnostic);

            parsed.Add(new ParsedDetection(
                objectIdResult.Value!,
                classNameResult.Value!,
                scoreResult.Value,
                zonesResult.Value!));
            index++;
        }

        var observedAt = timestampResult.Value;
        var events = new List<CloudEvent>(parsed.Count);
        foreach (var detection in parsed)
        {
            var identityFields = new[]
                {
                    _options.CameraId,
                    detection.ObjectId,
                    observedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                    detection.ClassName,
                    detection.Score.ToString("R", CultureInfo.InvariantCulture),
                    detection.Zones.Count.ToString(CultureInfo.InvariantCulture)
                }
                .Concat(detection.Zones)
                .ToArray();
            var cloudEvent = new CloudEvent(CloudEventsSpecVersion.V1_0)
            {
                Id = CameraEventId.FromFields(AdapterId, identityFields),
                Source = _options.Source,
                Type = CameraEventTypes.ObjectObserved,
                Subject = $"cameras/{EscapeSubjectSegment(_options.CameraId)}/objects/{EscapeSubjectSegment(detection.ObjectId)}",
                Time = observedAt,
                DataContentType = "application/json",
                DataSchema = CameraEventSchemas.CameraObjectObservationV1,
                Data = new CameraObjectObservationEventData(
                    AdapterId,
                    _options.CameraId,
                    detection.ObjectId,
                    detection.ClassName,
                    detection.Score,
                    detection.Zones,
                    observedAt)
            };

            var diagnostics = InteropCloudEventValidator.Validate(cloudEvent);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == AdapterDiagnosticSeverity.Error))
                return new AdapterResult(Array.Empty<CloudEvent>(), diagnostics);
            events.Add(cloudEvent);
        }

        return new AdapterResult(events, Array.Empty<AdapterDiagnostic>());
    }

    private static AdapterDiagnostic? ValidateJsonStructure(JsonElement root)
    {
        var nodes = 0;
        return ValidateJsonStructure(root, ref nodes);
    }

    private static AdapterDiagnostic? ValidateJsonStructure(JsonElement element, ref int nodes)
    {
        if (++nodes > MaxJsonNodes)
        {
            return AdapterDiagnostic.Error(
                "scrypted.json.too-complex",
                $"The Scrypted ObjectsDetected payload can contain at most {MaxJsonNodes} JSON values.",
                "$");
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    return AdapterDiagnostic.Error(
                        "scrypted.json.duplicate-property",
                        "The Scrypted ObjectsDetected payload contains a duplicate JSON property.",
                        "$");
                }

                var diagnostic = ValidateJsonStructure(property.Value, ref nodes);
                if (diagnostic is not null)
                    return diagnostic;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var diagnostic = ValidateJsonStructure(item, ref nodes);
                if (diagnostic is not null)
                    return diagnostic;
            }
        }

        return null;
    }

    private static ValueResult<string> ReadRequiredString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return ValueResult<string>.Invalid(RequiredDiagnostic($"The {name} field is required.", path));
        if (element.ValueKind != JsonValueKind.String)
            return ValueResult<string>.Invalid(InvalidDiagnostic($"The {name} field must be a string.", path));

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxIdentifierLength || value.Any(char.IsControl))
        {
            return ValueResult<string>.Invalid(
                InvalidDiagnostic($"The {name} field is empty, too long, or contains control characters.", path));
        }

        return ValueResult<string>.Valid(value);
    }

    private static ValueResult<DateTimeOffset> ReadRequiredUnixMilliseconds(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return ValueResult<DateTimeOffset>.Invalid(RequiredDiagnostic($"The {name} field is required.", path));
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt64(out var milliseconds) ||
            milliseconds < MinimumUnixMilliseconds)
        {
            return ValueResult<DateTimeOffset>.Invalid(
                InvalidDiagnostic(
                    $"The {name} field must be an integer Unix-millisecond timestamp on or after 2000-01-01T00:00:00Z.",
                    path));
        }

        try
        {
            return ValueResult<DateTimeOffset>.Valid(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds));
        }
        catch (ArgumentOutOfRangeException)
        {
            return ValueResult<DateTimeOffset>.Invalid(
                InvalidDiagnostic($"The {name} field is outside the supported timestamp range.", path));
        }
    }

    private static ValueResult<double> ReadRequiredConfidence(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return ValueResult<double>.Invalid(RequiredDiagnostic($"The {name} field is required.", path));
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value) ||
            value is < 0 or > 1)
        {
            return ValueResult<double>.Invalid(
                InvalidDiagnostic($"The {name} field must be a finite number from 0 through 1.", path));
        }

        return ValueResult<double>.Valid(value);
    }

    private static ValueResult<IReadOnlyList<string>> ReadStringArray(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return ValueResult<IReadOnlyList<string>>.Valid(Array.Empty<string>());
        if (element.ValueKind != JsonValueKind.Array)
            return ValueResult<IReadOnlyList<string>>.Invalid(InvalidDiagnostic($"The {name} field must be an array of strings.", path));
        if (element.GetArrayLength() > MaxZoneCount)
            return ValueResult<IReadOnlyList<string>>.Invalid(InvalidDiagnostic($"The {name} field contains too many entries.", path));

        var values = new List<string>(element.GetArrayLength());
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return ValueResult<IReadOnlyList<string>>.Invalid(
                    InvalidDiagnostic($"The {name} field must contain only strings.", $"{path}[{index}]"));
            }

            var value = item.GetString();
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxIdentifierLength || value.Any(char.IsControl))
            {
                return ValueResult<IReadOnlyList<string>>.Invalid(
                    InvalidDiagnostic($"The {name} field contains an empty, oversized, or invalid entry.", $"{path}[{index}]"));
            }

            values.Add(value);
            index++;
        }

        return ValueResult<IReadOnlyList<string>>.Valid(values);
    }

    private static bool IsJsonContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        var separatorIndex = contentType.IndexOf(';', StringComparison.Ordinal);
        var mediaType = separatorIndex < 0 ? contentType : contentType[..separatorIndex];
        return string.Equals(mediaType.Trim(), "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeSubjectSegment(string value) => Uri.EscapeDataString(value);

    private static AdapterResult InvalidField(string message, string path) => Failure(InvalidDiagnostic(message, path));

    private static AdapterDiagnostic RequiredDiagnostic(string message, string path) =>
        AdapterDiagnostic.Error("scrypted.field.required", message, path);

    private static AdapterDiagnostic InvalidDiagnostic(string message, string path) =>
        AdapterDiagnostic.Error("scrypted.field.invalid", message, path);

    private static AdapterResult Failure(AdapterDiagnostic diagnostic) =>
        new(Array.Empty<CloudEvent>(), new[] { diagnostic });

    private sealed record ParsedDetection(
        string ObjectId,
        string ClassName,
        double Score,
        IReadOnlyList<string> Zones);

    private readonly record struct ValueResult<T>(T? Value, AdapterDiagnostic? Diagnostic)
    {
        internal static ValueResult<T> Valid(T value) => new(value, null);

        internal static ValueResult<T> Invalid(AdapterDiagnostic diagnostic) => new(default, diagnostic);
    }
}
