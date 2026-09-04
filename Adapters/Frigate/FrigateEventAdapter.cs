using System.Text.Json;
using CloudNative.CloudEvents;

namespace OpenCamInterop.Adapters.Frigate;

public sealed class FrigateEventAdapter : ICameraEventAdapter
{
    public const string AdapterId = "frigate.events.v1";

    private const int MaxIdentifierLength = 512;
    private const int MaxZoneCount = 100;
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };
    private readonly FrigateAdapterOptions _options;

    public FrigateEventAdapter(FrigateAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public FrigateEventAdapter(Uri source, string topicPrefix = "frigate")
        : this(new FrigateAdapterOptions(source, topicPrefix))
    {
    }

    public string Id => AdapterId;

    public AdapterResult Adapt(AdapterMessage message)
    {
        if (message is null)
            return AdapterResult.Failure("message.required", "An adapter message is required.");

        if (!string.Equals(message.Channel, _options.EventsTopic, StringComparison.Ordinal))
        {
            return AdapterResult.Failure(
                "frigate.channel.unsupported",
                $"The adapter accepts only the '{_options.EventsTopic}' topic.",
                nameof(message.Channel));
        }

        if (!IsJsonContentType(message.ContentType))
        {
            return AdapterResult.Failure(
                "frigate.content-type.unsupported",
                "The Frigate events payload must use the application/json content type.",
                nameof(message.ContentType));
        }

        var payloadDiagnostic = AdapterPayloadGuard.Validate(message);
        if (payloadDiagnostic is not null)
        {
            return new AdapterResult(
                Array.Empty<CloudEvent>(),
                new[] { payloadDiagnostic });
        }

        try
        {
            using var document = JsonDocument.Parse(message.Payload, DocumentOptions);
            return AdaptDocument(message, document.RootElement);
        }
        catch (JsonException)
        {
            return AdapterResult.Failure(
                "frigate.json.invalid",
                "The Frigate events payload is not valid JSON.",
                "$");
        }
    }

    private AdapterResult AdaptDocument(AdapterMessage message, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return InvalidField("The Frigate events payload must be a JSON object.", "$");
        if (HasDuplicateProperties(root))
            return InvalidField("The Frigate events payload contains duplicate top-level properties.", "$");

        var typeResult = ReadRequiredString(root, "type", "$.type");
        if (typeResult.Diagnostic is not null)
            return Failure(typeResult.Diagnostic);

        var eventType = typeResult.Value switch
        {
            "new" => CameraEventTypes.ObjectDetected,
            "update" => CameraEventTypes.ObjectUpdated,
            "end" => CameraEventTypes.ObjectEnded,
            _ => null
        };
        if (eventType is null)
        {
            return AdapterResult.Failure(
                "frigate.type.unsupported",
                "The Frigate event type must be new, update, or end.",
                "$.type");
        }

        if (!root.TryGetProperty("after", out var after) || after.ValueKind != JsonValueKind.Object)
            return RequiredField("A Frigate event requires an after object.", "$.after");
        if (HasDuplicateProperties(after))
            return InvalidField("The Frigate after object contains duplicate properties.", "$.after");

        var objectIdResult = ReadRequiredString(after, "id", "$.after.id");
        if (objectIdResult.Diagnostic is not null)
            return Failure(objectIdResult.Diagnostic);

        var cameraResult = ReadRequiredString(after, "camera", "$.after.camera");
        if (cameraResult.Diagnostic is not null)
            return Failure(cameraResult.Diagnostic);

        var labelResult = ReadRequiredString(after, "label", "$.after.label");
        if (labelResult.Diagnostic is not null)
            return Failure(labelResult.Diagnostic);

        var startedAtResult = ReadRequiredUnixTimestamp(after, "start_time", "$.after.start_time");
        if (startedAtResult.Diagnostic is not null)
            return Failure(startedAtResult.Diagnostic);

        var endedAtResult = ReadOptionalUnixTimestamp(after, "end_time", "$.after.end_time");
        if (endedAtResult.Diagnostic is not null)
            return Failure(endedAtResult.Diagnostic);
        if (typeResult.Value == "end" && endedAtResult.Value is null)
            return RequiredField("An ended Frigate event requires end_time.", "$.after.end_time");
        if (endedAtResult.Value < startedAtResult.Value)
            return InvalidField("end_time cannot be earlier than start_time.", "$.after.end_time");

        var frameTimeResult = ReadOptionalUnixTimestamp(after, "frame_time", "$.after.frame_time");
        if (frameTimeResult.Diagnostic is not null)
            return Failure(frameTimeResult.Diagnostic);

        var confidenceResult = ReadOptionalConfidence(after, "score", "$.after.score");
        if (confidenceResult.Diagnostic is not null)
            return Failure(confidenceResult.Diagnostic);

        var topConfidenceResult = ReadOptionalConfidence(after, "top_score", "$.after.top_score");
        if (topConfidenceResult.Diagnostic is not null)
            return Failure(topConfidenceResult.Diagnostic);

        var currentZonesResult = ReadStringArray(after, "current_zones", "$.after.current_zones");
        if (currentZonesResult.Diagnostic is not null)
            return Failure(currentZonesResult.Diagnostic);

        var enteredZonesResult = ReadStringArray(after, "entered_zones", "$.after.entered_zones");
        if (enteredZonesResult.Diagnostic is not null)
            return Failure(enteredZonesResult.Diagnostic);

        var occurrenceTime = typeResult.Value == "end"
            ? endedAtResult.Value!.Value
            : frameTimeResult.Value ?? startedAtResult.Value!.Value;

        var cloudEvent = new CloudEvent(CloudEventsSpecVersion.V1_0)
        {
            Id = CameraEventId.FromAdapterMessage(AdapterId, message.Channel, message.Payload.Span),
            Source = _options.Source,
            Type = eventType,
            Subject = $"cameras/{EscapeSubjectSegment(cameraResult.Value!)}/objects/{EscapeSubjectSegment(objectIdResult.Value!)}",
            Time = occurrenceTime,
            DataContentType = "application/json",
            DataSchema = CameraEventSchemas.CameraObjectV1,
            Data = new CameraObjectEventData(
                AdapterId,
                cameraResult.Value!,
                objectIdResult.Value!,
                labelResult.Value!,
                confidenceResult.Value,
                topConfidenceResult.Value,
                currentZonesResult.Value!,
                enteredZonesResult.Value!,
                startedAtResult.Value!.Value,
                endedAtResult.Value)
        };

        var diagnostics = InteropCloudEventValidator.Validate(cloudEvent);
        return diagnostics.Any(diagnostic => diagnostic.Severity == AdapterDiagnosticSeverity.Error)
            ? new AdapterResult(Array.Empty<CloudEvent>(), diagnostics)
            : new AdapterResult(new[] { cloudEvent }, diagnostics);
    }

    private static ValueResult<string> ReadRequiredString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return ValueResult<string>.Invalid(RequiredDiagnostic($"The {name} field is required.", path));
        if (element.ValueKind != JsonValueKind.String)
            return ValueResult<string>.Invalid(InvalidDiagnostic($"The {name} field must be a string.", path));

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
            return ValueResult<string>.Invalid(InvalidDiagnostic($"The {name} field cannot be empty.", path));
        if (value.Length > MaxIdentifierLength || value.Any(char.IsControl))
        {
            return ValueResult<string>.Invalid(
                InvalidDiagnostic($"The {name} field is too long or contains control characters.", path));
        }

        return ValueResult<string>.Valid(value);
    }

    private static ValueResult<DateTimeOffset?> ReadRequiredUnixTimestamp(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return ValueResult<DateTimeOffset?>.Invalid(RequiredDiagnostic($"The {name} field is required.", path));

        return ParseUnixTimestamp(element, name, path);
    }

    private static ValueResult<DateTimeOffset?> ReadOptionalUnixTimestamp(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return ValueResult<DateTimeOffset?>.Valid(null);

        return ParseUnixTimestamp(element, name, path);
    }

    private static ValueResult<DateTimeOffset?> ParseUnixTimestamp(
        JsonElement element,
        string name,
        string path)
    {
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var seconds) ||
            !double.IsFinite(seconds))
        {
            return ValueResult<DateTimeOffset?>.Invalid(
                InvalidDiagnostic($"The {name} field must be a finite Unix timestamp.", path));
        }

        try
        {
            return ValueResult<DateTimeOffset?>.Valid(DateTimeOffset.UnixEpoch.AddSeconds(seconds));
        }
        catch (ArgumentOutOfRangeException)
        {
            return ValueResult<DateTimeOffset?>.Invalid(
                InvalidDiagnostic($"The {name} field is outside the supported timestamp range.", path));
        }
    }

    private static ValueResult<double?> ReadOptionalConfidence(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return ValueResult<double?>.Valid(null);
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value) ||
            value is < 0 or > 1)
        {
            return ValueResult<double?>.Invalid(
                InvalidDiagnostic($"The {name} field must be a finite number from 0 through 1.", path));
        }

        return ValueResult<double?>.Valid(value);
    }

    private static ValueResult<IReadOnlyList<string>> ReadStringArray(
        JsonElement parent,
        string name,
        string path)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return ValueResult<IReadOnlyList<string>>.Valid(Array.Empty<string>());
        if (element.ValueKind != JsonValueKind.Array)
        {
            return ValueResult<IReadOnlyList<string>>.Invalid(
                InvalidDiagnostic($"The {name} field must be an array of strings.", path));
        }
        if (element.GetArrayLength() > MaxZoneCount)
        {
            return ValueResult<IReadOnlyList<string>>.Invalid(
                InvalidDiagnostic($"The {name} field contains too many entries.", path));
        }

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
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > MaxIdentifierLength ||
                value.Any(char.IsControl))
            {
                return ValueResult<IReadOnlyList<string>>.Invalid(
                    InvalidDiagnostic(
                        $"The {name} field contains an empty, oversized, or invalid entry.",
                        $"{path}[{index}]"));
            }

            values.Add(value);
            index++;
        }

        return ValueResult<IReadOnlyList<string>>.Valid(values);
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(property => !names.Add(property.Name));
    }

    private static bool IsJsonContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        var separatorIndex = contentType.IndexOf(';', StringComparison.Ordinal);
        var mediaType = separatorIndex < 0 ? contentType : contentType[..separatorIndex];
        return string.Equals(mediaType.Trim(), "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeSubjectSegment(string value)
    {
        return Uri.EscapeDataString(value);
    }

    private static AdapterResult RequiredField(string message, string path)
    {
        return Failure(RequiredDiagnostic(message, path));
    }

    private static AdapterResult InvalidField(string message, string path)
    {
        return Failure(InvalidDiagnostic(message, path));
    }

    private static AdapterDiagnostic RequiredDiagnostic(string message, string path)
    {
        return AdapterDiagnostic.Error("frigate.field.required", message, path);
    }

    private static AdapterDiagnostic InvalidDiagnostic(string message, string path)
    {
        return AdapterDiagnostic.Error("frigate.field.invalid", message, path);
    }

    private static AdapterResult Failure(AdapterDiagnostic diagnostic)
    {
        return new AdapterResult(Array.Empty<CloudEvent>(), new[] { diagnostic });
    }

    private readonly record struct ValueResult<T>(T? Value, AdapterDiagnostic? Diagnostic)
    {
        internal static ValueResult<T> Valid(T value)
        {
            return new ValueResult<T>(value, null);
        }

        internal static ValueResult<T> Invalid(AdapterDiagnostic diagnostic)
        {
            return new ValueResult<T>(default, diagnostic);
        }
    }
}
