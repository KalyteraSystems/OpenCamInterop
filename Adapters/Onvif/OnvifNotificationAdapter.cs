using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using CloudNative.CloudEvents;

namespace OpenCamInterop.Adapters.Onvif;

public sealed class OnvifNotificationAdapter : ICameraEventAdapter
{
    public const string AdapterId = "onvif.notification.v1";

    private const string WsNotificationNamespace = "http://docs.oasis-open.org/wsn/b-2";
    private const string OnvifSchemaNamespace = "http://www.onvif.org/ver10/schema";
    private const string OnvifEventsNamespace = "http://www.onvif.org/ver10/events/wsdl";
    private const string OnvifTopicsNamespace = "http://www.onvif.org/ver10/topics";
    private const string OnvifConcreteSetDialect =
        "http://www.onvif.org/ver10/tev/topicExpression/ConcreteSet";
    private const string OasisConcreteDialect =
        "http://docs.oasis-open.org/wsn/t-1/TopicExpression/Concrete";
    private const string CellMotionTopic = "RuleEngine/CellMotionDetector/Motion";
    private const string RegionMotionTopic = "RuleEngine/MotionRegionDetector/Motion";
    private const int MaxXmlDepth = 64;
    private const int MaxNotifications = 128;
    private const int MaxItemsPerSection = 128;
    private const int MaxNameLength = 128;
    private const int MaxRawValueLength = 4096;
    private readonly OnvifAdapterOptions _options;

    public OnvifNotificationAdapter(OnvifAdapterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Id => AdapterId;

    public AdapterResult Adapt(AdapterMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payloadDiagnostic = AdapterPayloadGuard.Validate(message);
        if (payloadDiagnostic is not null)
            return new AdapterResult(Array.Empty<CloudEvent>(), new[] { payloadDiagnostic });

        if (!IsSupportedContentType(message.ContentType))
        {
            return AdapterResult.Failure(
                "content-type.unsupported",
                "ONVIF input must use application/soap+xml, application/xml, or text/xml.");
        }

        if (string.IsNullOrWhiteSpace(message.Channel) ||
            message.Channel.Length > 256 ||
            message.Channel.Any(char.IsControl))
        {
            return AdapterResult.Failure(
                "channel.invalid",
                "A channel of at most 256 non-control characters is required.");
        }

        try
        {
            var document = ReadDocument(message.Payload);
            return AdaptDocument(document);
        }
        catch (XmlException exception)
        {
            return AdapterResult.Failure("xml.invalid", SafeXmlError(exception));
        }
        catch (InvalidDataException exception)
        {
            return AdapterResult.Failure("xml.invalid", exception.Message);
        }
    }

    private AdapterResult AdaptDocument(XDocument document)
    {
        XNamespace wsnt = WsNotificationNamespace;
        var notifications = GetSupportedNotificationContainers(document)
            .SelectMany(container => container.Elements(wsnt + "NotificationMessage"))
            .Take(MaxNotifications + 1)
            .ToList();
        if (notifications.Count == 0)
        {
            return AdapterResult.Failure(
                "notification.missing",
                "No supported WS-Notification NotificationMessage element was found.");
        }
        if (notifications.Count > MaxNotifications)
        {
            return AdapterResult.Failure(
                "notification.too-many",
                $"An ONVIF payload can contain at most {MaxNotifications} notifications.");
        }

        var events = new List<CloudEvent>(notifications.Count);
        var diagnostics = new List<AdapterDiagnostic>();

        for (var index = 0; index < notifications.Count; index++)
        {
            var notification = notifications[index];
            var topicElements = notification.Elements(wsnt + "Topic").Take(2).ToList();
            if (topicElements.Count == 0)
            {
                return AdapterResult.Failure(
                    "notification.topic-missing",
                    "An ONVIF notification is missing its topic.",
                    $"notifications[{index}].topic");
            }
            if (topicElements.Count != 1)
            {
                return AdapterResult.Failure(
                    "notification.topic-ambiguous",
                    "An ONVIF notification must contain exactly one direct topic.",
                    $"notifications[{index}].topic");
            }
            if (string.IsNullOrWhiteSpace(topicElements[0].Value))
            {
                return AdapterResult.Failure(
                    "notification.topic-invalid",
                    "An ONVIF notification contains an empty topic.",
                    $"notifications[{index}].topic");
            }

            if (!TryResolveTopic(topicElements[0], out var topic))
            {
                return AdapterResult.Failure(
                    "notification.topic-invalid",
                    "An ONVIF notification contains an invalid concrete topic expression.",
                    $"notifications[{index}].topic");
            }

            var messageContainers = notification.Elements(wsnt + "Message").Take(2).ToList();
            if (messageContainers.Count == 0)
            {
                return AdapterResult.Failure(
                    "notification.message-missing",
                    "An ONVIF notification is missing its direct wsnt:Message/tt:Message payload.",
                    $"notifications[{index}].message");
            }
            if (messageContainers.Count != 1)
            {
                return AdapterResult.Failure(
                    "notification.message-ambiguous",
                    "An ONVIF notification must contain exactly one direct wsnt:Message/tt:Message payload.",
                    $"notifications[{index}].message");
            }
            var messageElements = messageContainers[0]
                .Elements(XName.Get("Message", OnvifSchemaNamespace))
                .Take(2)
                .ToList();
            if (messageElements.Count == 0)
            {
                return AdapterResult.Failure(
                    "notification.message-missing",
                    "An ONVIF notification is missing its direct wsnt:Message/tt:Message payload.",
                    $"notifications[{index}].message");
            }
            if (messageElements.Count != 1)
            {
                return AdapterResult.Failure(
                    "notification.message-ambiguous",
                    "An ONVIF notification must contain exactly one direct wsnt:Message/tt:Message payload.",
                    $"notifications[{index}].message");
            }
            var messageElement = messageElements[0];

            if (!TryParseTimestamp(messageElement.Attribute("UtcTime")?.Value, out var eventTime))
            {
                return AdapterResult.Failure(
                    "notification.time-invalid",
                    "An ONVIF notification requires a valid UtcTime value.",
                    $"notifications[{index}].utcTime");
            }

            var operation = NormalizeOperation(messageElement.Attribute("PropertyOperation")?.Value);
            if (messageElement.Attribute("PropertyOperation") is not null && operation is null)
            {
                diagnostics.Add(AdapterDiagnostic.Warning(
                    "notification.operation-unsupported",
                    "An unknown PropertyOperation was omitted from the normalized event.",
                    $"notifications[{index}].propertyOperation"));
            }

            var sourceItems = ReadSimpleItems(messageElement, "Source", index);
            var keyItems = ReadSimpleItems(messageElement, "Key", index);
            var dataItems = ReadSimpleItems(messageElement, "Data", index);

            if (messageElement.Descendants().Any(element => element.Name.LocalName == "ElementItem"))
            {
                diagnostics.Add(AdapterDiagnostic.Warning(
                    "notification.element-item-unsupported",
                    "ElementItem content is not mapped in v1.",
                    $"notifications[{index}]"));
            }

            var normalized = CreateCloudEvent(
                topic,
                operation,
                sourceItems,
                keyItems,
                dataItems,
                eventTime,
                diagnostics,
                index);
            var validation = InteropCloudEventValidator.Validate(normalized);
            diagnostics.AddRange(validation);
            if (validation.Any(item => item.Severity == AdapterDiagnosticSeverity.Error))
                return new AdapterResult(Array.Empty<CloudEvent>(), diagnostics);

            events.Add(normalized);
        }

        return new AdapterResult(events, diagnostics);
    }

    private CloudEvent CreateCloudEvent(
        ResolvedTopic topic,
        string? operation,
        IReadOnlyList<RawSimpleItem> sourceItems,
        IReadOnlyList<RawSimpleItem> keyItems,
        IReadOnlyList<RawSimpleItem> dataItems,
        DateTimeOffset eventTime,
        List<AdapterDiagnostic> diagnostics,
        int notificationIndex)
    {
        var isCanonicalMotionTopic = topic.IsRecognizedConcreteDialect &&
            topic.AllSegmentsInOnvifNamespace &&
            topic.Path is CellMotionTopic or RegionMotionTopic;
        var motionValueName = topic.Path == CellMotionTopic ? "IsMotion" : "State";
        var motionValues = dataItems
            .Where(item => string.Equals(
                LocalName(item.Name),
                motionValueName,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Value)
            .Take(2)
            .ToList();
        var isChanged = string.Equals(operation, "changed", StringComparison.Ordinal);
        var active = false;
        var isMotion = isCanonicalMotionTopic &&
            isChanged &&
            motionValues.Count == 1 &&
            TryParseBoolean(motionValues[0], out active);

        if (isCanonicalMotionTopic && isChanged && !isMotion)
        {
            diagnostics.Add(AdapterDiagnostic.Warning(
                "notification.motion-value-invalid",
                "A canonical motion change did not contain a recognized Boolean state and remains generic.",
                $"notifications[{notificationIndex}].data"));
        }

        var cameraId = CreateOpaqueIdentity(
            "camera",
            FindItemIdentity(sourceItems, "VideoSourceConfigurationToken", "VideoSourceToken", "VideoSource")) ?? "device";
        var ruleId = CreateOpaqueIdentity("rule", FindItemIdentity(sourceItems, "Rule"));
        object data = isMotion
            ? new CameraSignalEventData(AdapterId, cameraId, ruleId, "motion", active, "changed")
            : new OnvifNotificationEventData(
                AdapterId,
                PublicTopicNamespace(topic),
                topic.Path,
                operation,
                RedactAll(sourceItems),
                RedactAll(keyItems),
                RedactAll(dataItems));

        var eventId = CreateNotificationId(
            topic,
            operation,
            sourceItems,
            keyItems,
            dataItems,
            eventTime);
        var subject = isMotion
            ? $"cameras/{Uri.EscapeDataString(cameraId)}/signals/motion"
            : $"topics/{Uri.EscapeDataString(topic.Path)}";

        return new CloudEvent(CloudEventsSpecVersion.V1_0)
        {
            Id = eventId,
            Source = _options.Source,
            Type = isMotion ? CameraEventTypes.SignalChanged : CameraEventTypes.OnvifNotification,
            Subject = subject,
            Time = eventTime,
            DataContentType = "application/json",
            DataSchema = isMotion ? CameraEventSchemas.CameraSignalV1 : CameraEventSchemas.OnvifNotificationV1,
            Data = data
        };
    }

    private static IReadOnlyList<XElement> GetSupportedNotificationContainers(XDocument document)
    {
        XNamespace wsnt = WsNotificationNamespace;
        XNamespace tev = OnvifEventsNamespace;
        var root = document.Root;
        if (root is null)
            return Array.Empty<XElement>();
        if (root.Name == wsnt + "Notify" || root.Name == tev + "PullMessagesResponse")
            return new[] { root };

        var isSoapEnvelope = root.Name == XName.Get("Envelope", "http://www.w3.org/2003/05/soap-envelope") ||
            root.Name == XName.Get("Envelope", "http://schemas.xmlsoap.org/soap/envelope/");
        if (!isSoapEnvelope)
            return Array.Empty<XElement>();

        var bodyName = XName.Get("Body", root.Name.NamespaceName);
        var bodies = root.Elements(bodyName).Take(2).ToList();
        if (bodies.Count != 1)
            throw new InvalidDataException("A SOAP Envelope must contain exactly one direct Body element.");

        return bodies[0]
            .Elements()
            .Where(element => element.Name == wsnt + "Notify" || element.Name == tev + "PullMessagesResponse")
            .ToList();
    }

    private static XDocument ReadDocument(ReadOnlyMemory<byte> payload)
    {
        ValidateXmlDepth(payload);
        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, CreateReaderSettings());
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static void ValidateXmlDepth(ReadOnlyMemory<byte> payload)
    {
        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, CreateReaderSettings());
        while (reader.Read())
        {
            if (reader.Depth > MaxXmlDepth)
                throw new InvalidDataException($"XML nesting exceeds the {MaxXmlDepth}-level limit.");
        }
    }

    private static XmlReaderSettings CreateReaderSettings()
    {
        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = AdapterPayloadGuard.MaxPayloadBytes,
            XmlResolver = null
        };
    }

    private static IReadOnlyList<RawSimpleItem> ReadSimpleItems(
        XElement message,
        string sectionName,
        int notificationIndex)
    {
        var section = message.Element(XName.Get(sectionName, OnvifSchemaNamespace));
        if (section is null)
            return Array.Empty<RawSimpleItem>();

        var items = section.Elements(XName.Get("SimpleItem", OnvifSchemaNamespace)).ToList();
        if (items.Count > MaxItemsPerSection)
        {
            throw new InvalidDataException(
                $"notifications[{notificationIndex}].{sectionName} exceeds the {MaxItemsPerSection}-item limit.");
        }

        var result = new List<RawSimpleItem>(items.Count);
        foreach (var item in items)
        {
            var name = item.Attribute("Name")?.Value.Trim();
            var value = item.Attribute("Value")?.Value ?? string.Empty;
            if (!IsValidItemName(name) || value.Length > MaxRawValueLength)
            {
                throw new InvalidDataException(
                    $"notifications[{notificationIndex}].{sectionName} contains an invalid SimpleItem.");
            }

            result.Add(new RawSimpleItem(name!, value));
        }

        return result;
    }

    private static bool TryResolveTopic(XElement topicElement, out ResolvedTopic result)
    {
        var rawTopic = topicElement.Value.Trim();
        if (rawTopic.Length == 0 || rawTopic.Any(char.IsWhiteSpace))
        {
            result = default!;
            return false;
        }

        var parts = rawTopic.Split('/', StringSplitOptions.None);
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty))
        {
            result = default!;
            return false;
        }

        var normalizedParts = new List<string>(parts.Length);
        var canonicalParts = new List<string>(parts.Length);
        string? rootNamespace = null;
        var currentNamespace = string.Empty;
        var allOnvif = true;
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            var separator = part.IndexOf(':');
            string localName;
            string? itemNamespace = null;
            if (separator >= 0)
            {
                if (separator == 0 || separator == part.Length - 1 ||
                    part.IndexOf(':', separator + 1) >= 0)
                {
                    result = default!;
                    return false;
                }

                var prefix = part[..separator];
                localName = part[(separator + 1)..];
                itemNamespace = topicElement.GetNamespaceOfPrefix(prefix)?.NamespaceName;
                if (string.IsNullOrEmpty(itemNamespace))
                {
                    result = default!;
                    return false;
                }
            }
            else
            {
                localName = part;
            }

            if (!IsValidNcName(localName))
            {
                result = default!;
                return false;
            }

            if (index == 0)
                rootNamespace = itemNamespace ?? string.Empty;
            if (itemNamespace is not null)
                currentNamespace = itemNamespace;
            if (currentNamespace != OnvifTopicsNamespace)
                allOnvif = false;
            normalizedParts.Add(localName);
            canonicalParts.Add($"{{{currentNamespace}}}{localName}");
        }

        if (rootNamespace != OnvifTopicsNamespace)
            allOnvif = false;

        var dialect = topicElement.Attribute("Dialect")?.Value.Trim();
        result = new ResolvedTopic(
            rootNamespace ?? string.Empty,
            string.Join('/', normalizedParts),
            string.Join('/', canonicalParts),
            string.Equals(dialect, OnvifConcreteSetDialect, StringComparison.Ordinal) ||
                string.Equals(dialect, OasisConcreteDialect, StringComparison.Ordinal),
            allOnvif,
            dialect);
        return true;
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        var timeSeparator = value.IndexOf('T');
        var hasExplicitZone = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
            (timeSeparator >= 0 &&
             (value.LastIndexOf('+') > timeSeparator || value.LastIndexOf('-') > timeSeparator));
        if (!hasExplicitZone)
        {
            result = default;
            return false;
        }

        try
        {
            result = XmlConvert.ToDateTimeOffset(value).ToUniversalTime();
            return true;
        }
        catch (FormatException)
        {
            result = default;
            return false;
        }
        catch (ArgumentException)
        {
            result = default;
            return false;
        }
    }

    private static bool TryParseBoolean(string? value, out bool result)
    {
        if (bool.TryParse(value, out result))
            return true;
        if (value == "1")
        {
            result = true;
            return true;
        }
        if (value == "0")
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static string? NormalizeOperation(string? operation)
    {
        return operation?.Trim().ToLowerInvariant() switch
        {
            "initialized" => "initialized",
            "changed" => "changed",
            "deleted" => "deleted",
            _ => null
        };
    }

    private static string? FindItemIdentity(IReadOnlyList<RawSimpleItem> items, params string[] names)
    {
        var matches = items
            .Where(item => names.Contains(LocalName(item.Name), StringComparer.OrdinalIgnoreCase))
            .Select(item => new RawSimpleItem(LocalName(item.Name), item.Value))
            .ToArray();
        return matches.Length == 0 ? null : JsonSerializer.Serialize(matches);
    }

    private static string LocalName(string name)
    {
        var separator = name.LastIndexOf(':');
        return separator >= 0 ? name[(separator + 1)..] : name;
    }

    private static string? CreateOpaqueIdentity(string kind, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return CameraEventId.FromText($"onvif-{kind}", value);
    }

    private static string CreateNotificationId(
        ResolvedTopic topic,
        string? operation,
        IReadOnlyList<RawSimpleItem> sourceItems,
        IReadOnlyList<RawSimpleItem> keyItems,
        IReadOnlyList<RawSimpleItem> dataItems,
        DateTimeOffset eventTime)
    {
        var identity = new NotificationIdentity(
            topic.CanonicalPath,
            topic.Path,
            topic.Dialect,
            operation,
            eventTime,
            sourceItems,
            keyItems,
            dataItems);
        return CameraEventId.FromText(AdapterId, JsonSerializer.Serialize(identity));
    }

    private static IReadOnlyList<OnvifSimpleItem> RedactAll(IReadOnlyList<RawSimpleItem> items)
    {
        return items.Select(item => new OnvifSimpleItem(item.Name, "[redacted]")).ToArray();
    }

    private static string PublicTopicNamespace(ResolvedTopic topic)
    {
        return topic.AllSegmentsInOnvifNamespace
            ? OnvifTopicsNamespace
            : $"urn:opencaminterop:topic-namespace:{CameraEventId.FromText("namespace", topic.CanonicalPath)}";
    }

    private static bool IsValidItemName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxNameLength || value.Any(char.IsControl))
            return false;

        try
        {
            var parts = value.Split(':', StringSplitOptions.None);
            if (parts.Length is < 1 or > 2 || parts.Any(string.IsNullOrEmpty))
                return false;
            foreach (var part in parts)
                XmlConvert.VerifyNCName(part);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool IsValidNcName(string value)
    {
        try
        {
            XmlConvert.VerifyNCName(value);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool IsSupportedContentType(string value)
    {
        if (!MediaTypeHeaderValue.TryParse(value, out var parsed) || parsed.MediaType is null)
            return false;

        return parsed.MediaType.Equals("application/soap+xml", StringComparison.OrdinalIgnoreCase) ||
            parsed.MediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) ||
            parsed.MediaType.Equals("text/xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeXmlError(XmlException exception)
    {
        return exception.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase)
            ? "DTD declarations are prohibited."
            : "The ONVIF XML payload is malformed.";
    }

    private sealed record RawSimpleItem(string Name, string Value);

    private sealed record ResolvedTopic(
        string RootNamespace,
        string Path,
        string CanonicalPath,
        bool IsRecognizedConcreteDialect,
        bool AllSegmentsInOnvifNamespace,
        string? Dialect);

    private sealed record NotificationIdentity(
        string TopicIdentity,
        string Topic,
        string? Dialect,
        string? PropertyOperation,
        DateTimeOffset EventTime,
        IReadOnlyList<RawSimpleItem> SourceItems,
        IReadOnlyList<RawSimpleItem> KeyItems,
        IReadOnlyList<RawSimpleItem> DataItems);
}
