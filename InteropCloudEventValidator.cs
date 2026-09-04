using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudNative.CloudEvents;
using OpenCamInterop.Adapters;

namespace OpenCamInterop;

public static class InteropCloudEventValidator
{
    private static readonly JsonSerializerOptions ValidationJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static IReadOnlyList<AdapterDiagnostic> Validate(CloudEvent? cloudEvent)
    {
        var diagnostics = new List<AdapterDiagnostic>();
        if (cloudEvent is null)
        {
            diagnostics.Add(AdapterDiagnostic.Error("event.required", "A CloudEvent is required."));
            return diagnostics;
        }

        try
        {
            cloudEvent.Validate();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            diagnostics.Add(AdapterDiagnostic.Error("event.invalid", exception.Message));
        }

        if (cloudEvent.SpecVersion != CloudEventsSpecVersion.V1_0)
            diagnostics.Add(AdapterDiagnostic.Error("event.specversion", "CloudEvents specversion 1.0 is required."));

        ValidateSource(cloudEvent.Source, diagnostics);

        if (cloudEvent.Time is null)
            diagnostics.Add(AdapterDiagnostic.Error("event.time", "time is required for OpenCamInterop events."));

        if (!IsJsonContentType(cloudEvent.DataContentType))
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "event.datacontenttype",
                "datacontenttype must use the application/json media type."));
        }

        if (cloudEvent.DataSchema is null || !cloudEvent.DataSchema.IsAbsoluteUri)
            diagnostics.Add(AdapterDiagnostic.Error("event.dataschema", "dataschema must be an absolute URI."));
        else if (!string.IsNullOrEmpty(cloudEvent.DataSchema.UserInfo) ||
                 !string.IsNullOrEmpty(cloudEvent.DataSchema.Query) ||
                 !string.IsNullOrEmpty(cloudEvent.DataSchema.Fragment))
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "event.dataschema-components",
                "dataschema must not contain user information, a query string, or a fragment."));
        }

        if (cloudEvent.Data is null)
        {
            diagnostics.Add(AdapterDiagnostic.Error("event.data", "data is required."));
            return diagnostics;
        }

        ValidateDataObjectShape(cloudEvent.Data, diagnostics);

        if (cloudEvent.Type is not null &&
            (!cloudEvent.Type.StartsWith("com.kalyterasystems.", StringComparison.Ordinal) ||
             !cloudEvent.Type.EndsWith(".v1", StringComparison.Ordinal)))
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "event.type",
                "type must use a versioned com.kalyterasystems event name ending in .v1."));
        }

        switch (cloudEvent.Type)
        {
            case CameraEventTypes.ObjectDetected:
            case CameraEventTypes.ObjectUpdated:
            case CameraEventTypes.ObjectEnded:
                RequireSchema(cloudEvent, CameraEventSchemas.CameraObjectV1, diagnostics);
                if (TryReadData(
                    cloudEvent.Data,
                    "camera object",
                    diagnostics,
                    out CameraObjectEventData? objectData,
                    "adapter",
                    "cameraId",
                    "objectId",
                    "label",
                    "currentZones",
                    "enteredZones",
                    "startedAt"))
                {
                    ValidateObjectData(objectData!, diagnostics);
                }
                break;

            case CameraEventTypes.SignalChanged:
                RequireSchema(cloudEvent, CameraEventSchemas.CameraSignalV1, diagnostics);
                if (TryReadData(
                    cloudEvent.Data,
                    "camera signal",
                    diagnostics,
                    out CameraSignalEventData? signalData,
                    "adapter",
                    "cameraId",
                    "signal",
                    "active",
                    "propertyOperation"))
                {
                    ValidateSignalData(signalData!, diagnostics);
                }
                break;

            case CameraEventTypes.OnvifNotification:
                RequireSchema(cloudEvent, CameraEventSchemas.OnvifNotificationV1, diagnostics);
                if (TryReadData(
                    cloudEvent.Data,
                    "ONVIF notification",
                    diagnostics,
                    out OnvifNotificationEventData? notificationData,
                    "adapter",
                    "topicNamespace",
                    "topic",
                    "sourceItems",
                    "keyItems",
                    "dataItems"))
                {
                    ValidateOnvifData(notificationData!, diagnostics);
                }
                break;
        }

        return diagnostics;
    }

    public static void EnsureValid(CloudEvent cloudEvent)
    {
        var diagnostics = Validate(cloudEvent);
        var errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == AdapterDiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Message)
            .ToArray();

        if (errors.Length > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
    }

    private static void ValidateSource(Uri? source, List<AdapterDiagnostic> diagnostics)
    {
        if (source is null || !source.IsAbsoluteUri)
        {
            diagnostics.Add(AdapterDiagnostic.Error("event.source", "source must be an absolute URI."));
            return;
        }

        if (!string.IsNullOrEmpty(source.UserInfo))
            diagnostics.Add(AdapterDiagnostic.Error("event.source-userinfo", "source must not contain user information."));
        if (!string.IsNullOrEmpty(source.Query) || !string.IsNullOrEmpty(source.Fragment))
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "event.source-components",
                "source must not contain a query string or fragment."));
        }
    }

    private static bool IsJsonContentType(string? contentType)
    {
        return MediaTypeHeaderValue.TryParse(contentType, out var parsed) &&
            string.Equals(parsed.MediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireSchema(
        CloudEvent cloudEvent,
        Uri expected,
        List<AdapterDiagnostic> diagnostics)
    {
        if (cloudEvent.DataSchema != expected)
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "event.dataschema-mismatch",
                $"dataschema must be {expected} for type {cloudEvent.Type}."));
        }
    }

    private static bool TryReadData<T>(
        object data,
        string description,
        List<AdapterDiagnostic> diagnostics,
        out T? value,
        params string[] requiredProperties)
        where T : class
    {
        if (data is T typed)
        {
            value = typed;
            return true;
        }

        if (data is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "event.data-shape",
                $"data must be a {description} JSON object.",
                "data"));
            value = null;
            return false;
        }

        if (requiredProperties.Any(property => !element.TryGetProperty(property, out _)))
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "event.data-required",
                $"data is missing a required {description} v1 property.",
                "data"));
            value = null;
            return false;
        }

        try
        {
            value = element.Deserialize<T>(ValidationJsonOptions);
            if (value is not null)
                return true;
        }
        catch (JsonException)
        {
            // Report a stable diagnostic without reflecting untrusted payload content.
        }

        diagnostics.Add(AdapterDiagnostic.Error(
            "event.data-invalid",
            $"data does not match the {description} v1 contract.",
            "data"));
        value = null;
        return false;
    }

    private static void ValidateDataObjectShape(
        object data,
        List<AdapterDiagnostic> diagnostics)
    {
        if (data is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(AdapterDiagnostic.Error(
                    "event.data-shape",
                    "data must be a JSON object.",
                    "data"));
            }
            return;
        }

        try
        {
            if (JsonSerializer.SerializeToElement(data, data.GetType(), ValidationJsonOptions).ValueKind ==
                JsonValueKind.Object)
            {
                return;
            }
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // Report a stable diagnostic without reflecting untrusted payload content.
        }

        diagnostics.Add(AdapterDiagnostic.Error(
            "event.data-shape",
            "data must serialize as a JSON object.",
            "data"));
    }

    private static void ValidateObjectData(
        CameraObjectEventData data,
        List<AdapterDiagnostic> diagnostics)
    {
        ValidateRequired(data.Adapter, "adapter", diagnostics);
        ValidateRequired(data.CameraId, "cameraId", diagnostics);
        ValidateRequired(data.ObjectId, "objectId", diagnostics);
        ValidateRequired(data.Label, "label", diagnostics);
        ValidateConfidence(data.Confidence, "confidence", diagnostics);
        ValidateConfidence(data.TopConfidence, "topConfidence", diagnostics);

        ValidateStringList(data.CurrentZones, "currentZones", 100, diagnostics);
        ValidateStringList(data.EnteredZones, "enteredZones", 100, diagnostics);
        if (data.EndedAt < data.StartedAt)
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "data.endedAt",
                "data.endedAt cannot be earlier than data.startedAt.",
                "data.endedAt"));
        }
    }

    private static void ValidateSignalData(
        CameraSignalEventData data,
        List<AdapterDiagnostic> diagnostics)
    {
        ValidateRequired(data.Adapter, "adapter", diagnostics);
        ValidateRequired(data.CameraId, "cameraId", diagnostics);
        ValidateRequired(data.Signal, "signal", diagnostics);
        if (data.RuleId is not null)
            ValidateRequired(data.RuleId, "ruleId", diagnostics);
        if (!string.Equals(data.PropertyOperation, "changed", StringComparison.Ordinal))
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "data.propertyOperation",
                "data.propertyOperation must be changed for a signal event.",
                "data.propertyOperation"));
        }
    }

    private static void ValidateOnvifData(
        OnvifNotificationEventData data,
        List<AdapterDiagnostic> diagnostics)
    {
        ValidateRequired(data.Adapter, "adapter", diagnostics);
        ValidateRequired(data.Topic, "topic", diagnostics);
        if (data.TopicNamespace is null)
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "data.topicNamespace",
                "data.topicNamespace is required.",
                "data.topicNamespace"));
        }

        if (data.PropertyOperation is not null &&
            data.PropertyOperation is not ("initialized" or "changed" or "deleted"))
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                "data.propertyOperation",
                "data.propertyOperation must be initialized, changed, or deleted.",
                "data.propertyOperation"));
        }

        ValidateOnvifItems(data.SourceItems, "sourceItems", diagnostics);
        ValidateOnvifItems(data.KeyItems, "keyItems", diagnostics);
        ValidateOnvifItems(data.DataItems, "dataItems", diagnostics);
    }

    private static void ValidateOnvifItems(
        IReadOnlyList<OnvifSimpleItem>? items,
        string name,
        List<AdapterDiagnostic> diagnostics)
    {
        if (items is null || items.Count > 128)
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                $"data.{name}",
                $"data.{name} is required and must contain at most 128 entries.",
                $"data.{name}"));
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item is null || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 128)
            {
                diagnostics.Add(AdapterDiagnostic.Error(
                    $"data.{name}.name",
                    $"data.{name}[{index}].name is required and must contain at most 128 characters.",
                    $"data.{name}[{index}].name"));
            }
            if (item is not null && !string.Equals(item.Value, "[redacted]", StringComparison.Ordinal))
            {
                diagnostics.Add(AdapterDiagnostic.Error(
                    $"data.{name}.value",
                    $"data.{name}[{index}].value must be redacted in generic ONVIF events.",
                    $"data.{name}[{index}].value"));
            }
        }
    }

    private static void ValidateStringList(
        IReadOnlyList<string>? values,
        string name,
        int maximumCount,
        List<AdapterDiagnostic> diagnostics)
    {
        if (values is null || values.Count > maximumCount || values.Any(string.IsNullOrWhiteSpace))
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                $"data.{name}",
                $"data.{name} is required, must contain no empty entries, and must contain at most {maximumCount} entries.",
                $"data.{name}"));
        }
    }

    private static void ValidateRequired(
        string? value,
        string name,
        List<AdapterDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                $"data.{name}",
                $"data.{name} is required.",
                $"data.{name}"));
        }
    }

    private static void ValidateConfidence(
        double? value,
        string name,
        List<AdapterDiagnostic> diagnostics)
    {
        if (value.HasValue && (!double.IsFinite(value.Value) || value.Value is < 0 or > 1))
        {
            diagnostics.Add(AdapterDiagnostic.Error(
                $"data.{name}",
                $"data.{name} must be between 0 and 1.",
                $"data.{name}"));
        }
    }
}
