using System.Text;
using System.Text.Json;
using CloudNative.CloudEvents;
using OpenCamInterop;
using OpenCamInterop.Adapters;
using OpenCamInterop.Adapters.Frigate;
using OpenCamInterop.Adapters.Onvif;

namespace OpenCamInterop.Tests;

public sealed class OpenCamInteropAdapterTests
{
    private static readonly DateTimeOffset ReceivedAt =
        new(2026, 9, 4, 10, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("new", CameraEventTypes.ObjectDetected, null)]
    [InlineData("update", CameraEventTypes.ObjectUpdated, null)]
    [InlineData("end", CameraEventTypes.ObjectEnded, 1_725_445_805.25)]
    public void FrigateMapsSupportedObjectLifecycleEvents(
        string nativeType,
        string expectedType,
        double? endTime)
    {
        var adapter = CreateFrigateAdapter();
        var payload = nativeType == "new"
            ? ReadFixture("frigate", "object-new.json")
            : FrigatePayload(nativeType, endTime);

        var result = adapter.Adapt(JsonMessage("frigate/events", payload));

        Assert.True(result.IsSuccess);
        var cloudEvent = Assert.Single(result.Events);
        Assert.Equal(expectedType, cloudEvent.Type);
        Assert.Equal(new Uri("urn:camera:frigate-lab"), cloudEvent.Source);
        Assert.Equal("cameras/front%20door/objects/track-42", cloudEvent.Subject);
        Assert.Equal(CameraEventSchemas.CameraObjectV1, cloudEvent.DataSchema);
        var data = Assert.IsType<CameraObjectEventData>(cloudEvent.Data);
        Assert.Equal("frigate.events.v1", data.Adapter);
        Assert.Equal("front door", data.CameraId);
        Assert.Equal("track-42", data.ObjectId);
        Assert.Equal("person", data.Label);
        Assert.Equal(0.82, data.Confidence);
        Assert.Equal(0.91, data.TopConfidence);
        Assert.Equal(new[] { "porch" }, data.CurrentZones);
        Assert.Equal(new[] { "driveway", "porch" }, data.EnteredZones);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1_725_445_800.25), data.StartedAt);
        Assert.Equal(
            endTime is null ? null : DateTimeOffset.UnixEpoch.AddSeconds(endTime.Value),
            data.EndedAt);
    }

    [Fact]
    public void FrigateUsesExactMessageIdentityAndOmitsPrivateNativeFields()
    {
        var adapter = CreateFrigateAdapter();
        var payload = FrigatePayload("update", null);
        var first = adapter.Adapt(JsonMessage("frigate/events", payload));
        var redelivery = adapter.Adapt(JsonMessage("frigate/events", payload));
        var changed = adapter.Adapt(JsonMessage(
            "frigate/events",
            payload.Replace("0.82", "0.83", StringComparison.Ordinal)));

        var firstEvent = Assert.Single(first.Events);
        Assert.Equal(firstEvent.Id, Assert.Single(redelivery.Events).Id);
        Assert.NotEqual(firstEvent.Id, Assert.Single(changed.Events).Id);

        var encoded = Encoding.UTF8.GetString(StructuredCloudEventJson.Serialize(firstEvent).Span);
        Assert.Contains("\"cameraId\":\"front door\"", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("sub_label", encoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recognized_license_plate", encoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-person", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST-PLATE", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void FrigateUsesStartTimeAsDeterministicOccurrenceFallback()
    {
        var payload = FrigatePayload("update", null)
            .Replace("\"frame_time\": 1725445802.5,", string.Empty, StringComparison.Ordinal);

        var first = Assert.Single(CreateFrigateAdapter().Adapt(JsonMessage("frigate/events", payload)).Events);
        var laterDelivery = Assert.Single(CreateFrigateAdapter().Adapt(new AdapterMessage(
            "frigate/events",
            Encoding.UTF8.GetBytes(payload),
            "application/json",
            ReceivedAt.AddHours(2))).Events);

        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1_725_445_800.25), first.Time);
        Assert.Equal(first.Time, laterDelivery.Time);
        Assert.Equal(first.Id, laterDelivery.Id);
    }

    [Theory]
    [InlineData("other/events", "application/json", "frigate.channel.unsupported")]
    [InlineData("frigate/events", "text/plain", "frigate.content-type.unsupported")]
    [InlineData("frigate/events", "application/json", "frigate.json.invalid")]
    public void FrigateRejectsUnsupportedOrMalformedMessages(
        string channel,
        string contentType,
        string expectedCode)
    {
        var payload = expectedCode == "frigate.json.invalid" ? "{" : FrigatePayload("new", null);

        var result = CreateFrigateAdapter().Adapt(new AdapterMessage(
            channel,
            Encoding.UTF8.GetBytes(payload),
            contentType,
            ReceivedAt));

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Events);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }

    [Fact]
    public void FrigateRejectsMissingRequiredFieldsAndOversizedPayloads()
    {
        var adapter = CreateFrigateAdapter();
        var missing = adapter.Adapt(JsonMessage(
            "frigate/events",
            """{"type":"new","after":{"id":"track-42"}}"""));
        var oversized = adapter.Adapt(new AdapterMessage(
            "frigate/events",
            new byte[(1024 * 1024) + 1],
            "application/json",
            ReceivedAt));

        Assert.Contains(missing.Diagnostics, diagnostic =>
            diagnostic.Code == "frigate.field.required" && diagnostic.Path == "$.after.camera");
        Assert.Contains(oversized.Diagnostics, diagnostic => diagnostic.Code == "payload.too-large");
    }

    [Fact]
    public void AdapterSourcesRejectCredentialsQueriesAndFragments()
    {
        Assert.Throws<ArgumentException>(() =>
            new FrigateAdapterOptions(new Uri("https://user:secret@example.test/camera")));
        Assert.Throws<ArgumentException>(() =>
            new FrigateAdapterOptions(new Uri("https://example.test/camera?token=secret")));
        Assert.Throws<ArgumentException>(() =>
            new OnvifAdapterOptions(new Uri("https://example.test/camera#private")));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("0", false)]
    public void OnvifMapsChangedMotionWithArbitraryTopicPrefix(string nativeValue, bool expectedActive)
    {
        var adapter = CreateOnvifAdapter();
        var payload = nativeValue == "true"
            ? ReadFixture("onvif", "cell-motion-changed.xml")
            : OnvifMotionEnvelope("Changed", nativeValue, "vendor42");
        var result = adapter.Adapt(XmlMessage(payload));

        Assert.True(result.IsSuccess);
        var cloudEvent = Assert.Single(result.Events);
        Assert.Equal(CameraEventTypes.SignalChanged, cloudEvent.Type);
        Assert.StartsWith("cameras/onvif-camera-", cloudEvent.Subject, StringComparison.Ordinal);
        Assert.EndsWith("/signals/motion", cloudEvent.Subject, StringComparison.Ordinal);
        Assert.Equal(CameraEventSchemas.CameraSignalV1, cloudEvent.DataSchema);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 10, 15, 30, TimeSpan.Zero), cloudEvent.Time);
        var data = Assert.IsType<CameraSignalEventData>(cloudEvent.Data);
        Assert.StartsWith("onvif-camera-", data.CameraId, StringComparison.Ordinal);
        Assert.Equal("motion", data.Signal);
        Assert.Equal(expectedActive, data.Active);
        Assert.Equal("changed", data.PropertyOperation);
    }

    [Fact]
    public void OnvifMapsPullPointRegionMotionFixture()
    {
        var result = CreateOnvifAdapter().Adapt(XmlMessage(
            ReadFixture("onvif", "pullpoint-region-motion.xml")));

        Assert.True(result.IsSuccess);
        var cloudEvent = Assert.Single(result.Events);
        Assert.Equal(CameraEventTypes.SignalChanged, cloudEvent.Type);
        var data = Assert.IsType<CameraSignalEventData>(cloudEvent.Data);
        Assert.True(data.Active);
        Assert.StartsWith("onvif-rule-", data.RuleId, StringComparison.Ordinal);
    }

    [Fact]
    public void OnvifPreservesInitializedMotionWithoutEmittingATrigger()
    {
        var result = CreateOnvifAdapter().Adapt(XmlMessage(OnvifMotionEnvelope(
            "Initialized",
            "true",
            "tns")));

        var cloudEvent = Assert.Single(result.Events);
        Assert.Equal(CameraEventTypes.OnvifNotification, cloudEvent.Type);
        Assert.Equal(CameraEventSchemas.OnvifNotificationV1, cloudEvent.DataSchema);
        var data = Assert.IsType<OnvifNotificationEventData>(cloudEvent.Data);
        Assert.Equal("initialized", data.PropertyOperation);
        Assert.Equal("http://www.onvif.org/ver10/topics", data.TopicNamespace);
        Assert.Equal("RuleEngine/CellMotionDetector/Motion", data.Topic);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void OnvifDoesNotPromoteVendorOrUnknownDialectTopics(
        bool useVendorNamespace,
        bool omitDialect)
    {
        var xml = OnvifMotionEnvelope("Changed", "true", "tns");
        if (useVendorNamespace)
        {
            xml = xml.Replace(
                "xmlns:tns=\"http://www.onvif.org/ver10/topics\"",
                "xmlns:tns=\"urn:vendor:similar-topics\"",
                StringComparison.Ordinal);
        }
        if (omitDialect)
        {
            xml = xml.Replace(
                " Dialect=\"http://www.onvif.org/ver10/tev/topicExpression/ConcreteSet\"",
                string.Empty,
                StringComparison.Ordinal);
        }

        var result = CreateOnvifAdapter().Adapt(XmlMessage(xml));

        Assert.True(result.IsSuccess);
        Assert.Equal(CameraEventTypes.OnvifNotification, Assert.Single(result.Events).Type);
    }

    [Fact]
    public void OnvifIgnoresNotificationContainersNestedInExtensionData()
    {
        var nested = $$"""
            <root xmlns:wsnt="http://docs.oasis-open.org/wsn/b-2">
              <extension>
                <wsnt:Notify>
                  {{OnvifMotionNotification("tns", "Changed", "true")}}
                </wsnt:Notify>
              </extension>
            </root>
            """;

        var result = CreateOnvifAdapter().Adapt(XmlMessage(nested));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "notification.missing");
    }

    [Fact]
    public void OnvifRejectsDuplicateEnvelopeFieldsAndDoesNotPromoteDuplicateStates()
    {
        var valid = OnvifMotionEnvelope("Changed", "true", "tns");
        var duplicateTopic = valid.Replace(
            "<wsnt:Topic ",
            "<wsnt:Topic Dialect=\"http://www.onvif.org/ver10/tev/topicExpression/ConcreteSet\">tns:Device/Trigger</wsnt:Topic><wsnt:Topic ",
            StringComparison.Ordinal);
        var duplicateMessage = valid.Replace(
            "<wsnt:Message>",
            "<wsnt:Message><tt:Message UtcTime=\"2026-09-04T10:15:29Z\" /></wsnt:Message><wsnt:Message>",
            StringComparison.Ordinal);
        var duplicateState = valid.Replace(
            "<tt:SimpleItem Name=\"IsMotion\" Value=\"true\" />",
            "<tt:SimpleItem Name=\"IsMotion\" Value=\"true\" /><tt:SimpleItem Name=\"IsMotion\" Value=\"false\" />",
            StringComparison.Ordinal);

        var topicResult = CreateOnvifAdapter().Adapt(XmlMessage(duplicateTopic));
        var messageResult = CreateOnvifAdapter().Adapt(XmlMessage(duplicateMessage));
        var stateResult = CreateOnvifAdapter().Adapt(XmlMessage(duplicateState));

        Assert.Contains(topicResult.Diagnostics, diagnostic => diagnostic.Code == "notification.topic-ambiguous");
        Assert.Contains(messageResult.Diagnostics, diagnostic => diagnostic.Code == "notification.message-ambiguous");
        Assert.Equal(CameraEventTypes.OnvifNotification, Assert.Single(stateResult.Events).Type);
        Assert.Contains(stateResult.Diagnostics, diagnostic => diagnostic.Code == "notification.motion-value-invalid");
    }

    [Fact]
    public void OnvifPreservesDuplicateItemsWhileRedactingSensitiveValues()
    {
        const string xml = """
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:wsnt="http://docs.oasis-open.org/wsn/b-2"
                        xmlns:tt="http://www.onvif.org/ver10/schema"
                        xmlns:custom="urn:example:custom-topics">
              <s:Body>
                <wsnt:Notify>
                  <wsnt:NotificationMessage>
                    <wsnt:Topic>custom:Device/Analytics/Counter</wsnt:Topic>
                    <wsnt:Message>
                      <tt:Message UtcTime="2026-09-04T10:15:30Z" PropertyOperation="Changed">
                        <tt:Source>
                          <tt:SimpleItem Name="VideoSourceConfigurationToken" Value="private-token" />
                        </tt:Source>
                        <tt:Key>
                          <tt:SimpleItem Name="Rule" Value="first" />
                          <tt:SimpleItem Name="Rule" Value="second" />
                        </tt:Key>
                        <tt:Data>
                          <tt:SimpleItem Name="Count" Value="3" />
                          <tt:SimpleItem Name="Endpoint" Value="https://user:secret@example.test/live" />
                          <tt:ElementItem Name="VendorExtension"><custom:Value>ignored</custom:Value></tt:ElementItem>
                        </tt:Data>
                      </tt:Message>
                    </wsnt:Message>
                  </wsnt:NotificationMessage>
                </wsnt:Notify>
              </s:Body>
            </s:Envelope>
            """;

        var result = CreateOnvifAdapter().Adapt(XmlMessage(xml));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "notification.element-item-unsupported" &&
            diagnostic.Severity == AdapterDiagnosticSeverity.Warning);
        var data = Assert.IsType<OnvifNotificationEventData>(Assert.Single(result.Events).Data);
        Assert.StartsWith("urn:opencaminterop:topic-namespace:", data.TopicNamespace, StringComparison.Ordinal);
        Assert.Equal(new[] { "[redacted]", "[redacted]" }, data.KeyItems.Select(item => item.Value));
        Assert.Equal("[redacted]", Assert.Single(data.SourceItems).Value);
        Assert.Equal("[redacted]", data.DataItems.Single(item => item.Name == "Endpoint").Value);
        Assert.Equal("[redacted]", data.DataItems.Single(item => item.Name == "Count").Value);
    }

    [Fact]
    public void OnvifUsesExactMessageIdentityForBatches()
    {
        var adapter = CreateOnvifAdapter();
        var one = OnvifMotionNotification("tns", "Changed", "true");
        var payload = OnvifEnvelope(one + one);

        var first = adapter.Adapt(XmlMessage(payload));
        var redelivery = adapter.Adapt(XmlMessage(payload));

        Assert.Equal(2, first.Events.Count);
        Assert.Equal(first.Events.Select(item => item.Id), redelivery.Events.Select(item => item.Id));
        Assert.Equal(first.Events[0].Id, first.Events[1].Id);
    }

    [Fact]
    public void OnvifIdentityIgnoresSoapWrapperFormatting()
    {
        var notification = OnvifMotionNotification("tns", "Changed", "true");
        var compact = CreateOnvifAdapter().Adapt(XmlMessage(OnvifEnvelope(notification)));
        var reformatted = CreateOnvifAdapter().Adapt(XmlMessage(
            OnvifEnvelope(notification).Replace("<s:Body>", "<s:Body>\n<!-- delivery wrapper changed -->", StringComparison.Ordinal)));

        Assert.Equal(Assert.Single(compact.Events).Id, Assert.Single(reformatted.Events).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-time")]
    [InlineData("2026-09-04T10:15:30")]
    public void OnvifRejectsMissingOrInvalidOccurrenceTime(string utcTime)
    {
        var xml = OnvifMotionEnvelope("Changed", "true", "tns");
        xml = utcTime.Length == 0
            ? xml.Replace(" UtcTime=\"2026-09-04T10:15:30Z\"", string.Empty, StringComparison.Ordinal)
            : xml.Replace("2026-09-04T10:15:30Z", utcTime, StringComparison.Ordinal);

        var result = CreateOnvifAdapter().Adapt(XmlMessage(xml));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "notification.time-invalid");
    }

    [Theory]
    [InlineData("<broken", "xml.invalid")]
    [InlineData("<!DOCTYPE foo [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><foo>&xxe;</foo>", "xml.invalid")]
    [InlineData("<root />", "notification.missing")]
    public void OnvifRejectsMalformedUnsafeOrUnrecognizedXml(string xml, string expectedCode)
    {
        var result = CreateOnvifAdapter().Adapt(XmlMessage(xml));

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Events);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
        Assert.DoesNotContain("/etc/passwd", string.Join(' ', result.Diagnostics.Select(item => item.Message)));
    }

    [Fact]
    public void StructuredCloudEventBatchRoundTripsWithCamelCaseData()
    {
        var adapted = CreateFrigateAdapter().Adapt(JsonMessage(
            "frigate/events",
            FrigatePayload("new", null)));
        var original = Assert.Single(adapted.Events);

        var bytes = StructuredCloudEventJson.SerializeBatch(new[] { original });
        var json = Encoding.UTF8.GetString(bytes.Span);
        var decoded = Assert.Single(StructuredCloudEventJson.DeserializeBatch(bytes));

        Assert.StartsWith("[", json, StringComparison.Ordinal);
        Assert.Contains("\"cameraId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CameraId\"", json, StringComparison.Ordinal);
        Assert.Equal(original.Id, decoded.Id);
        Assert.Equal(original.Source, decoded.Source);
        Assert.Equal(original.Type, decoded.Type);
        Assert.IsType<JsonElement>(decoded.Data);
    }

    [Fact]
    public void EveryKnownV1DataContractRoundTripsThroughStructuredJson()
    {
        var objectEvent = Assert.Single(CreateFrigateAdapter().Adapt(JsonMessage(
            "frigate/events",
            ReadFixture("frigate", "object-new.json"))).Events);
        var signalEvent = Assert.Single(CreateOnvifAdapter().Adapt(XmlMessage(
            ReadFixture("onvif", "cell-motion-changed.xml"))).Events);
        var genericEvent = Assert.Single(CreateOnvifAdapter().Adapt(XmlMessage(
            OnvifMotionEnvelope("Initialized", "true", "tns"))).Events);

        var encoded = StructuredCloudEventJson.SerializeBatch(
            new[] { objectEvent, signalEvent, genericEvent });
        var decoded = StructuredCloudEventJson.DeserializeBatch(encoded);

        Assert.Equal(
            new[]
            {
                CameraEventTypes.ObjectDetected,
                CameraEventTypes.SignalChanged,
                CameraEventTypes.OnvifNotification
            },
            decoded.Select(cloudEvent => cloudEvent.Type));
    }

    [Fact]
    public void StructuredDecoderRejectsDuplicateEnvelopeMembers()
    {
        var validBytes = StructuredCloudEventJson.Serialize(
            Assert.Single(CreateFrigateAdapter().Adapt(JsonMessage(
                "frigate/events",
                FrigatePayload("new", null))).Events));
        var valid = Encoding.UTF8.GetString(validBytes.Span);
        var duplicate = valid.Replace(
            "\"id\":",
            "\"id\":\"shadow\",\"id\":",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            StructuredCloudEventJson.Deserialize(Encoding.UTF8.GetBytes(duplicate)));
    }

    [Fact]
    public void StructuredDecoderValidatesDecodedDataAndSchema()
    {
        var invalidData = """
            {
              "specversion":"1.0",
              "id":"signal-1",
              "source":"urn:camera:test",
              "type":"com.kalyterasystems.opencaminterop.signal.changed.v1",
              "time":"2026-09-04T10:15:30Z",
              "datacontenttype":"application/json; charset=utf-8",
              "dataschema":"urn:opencaminterop:schema:camera-signal-event:1",
              "data":{"adapter":"test","cameraId":"camera","signal":"motion","active":"yes","propertyOperation":"changed"}
            }
            """;
        var wrongSchema = invalidData
            .Replace("\"active\":\"yes\"", "\"active\":true", StringComparison.Ordinal)
            .Replace("camera-signal-event:1", "camera-object-event:1", StringComparison.Ordinal);
        var missingActive = invalidData
            .Replace("\"active\":\"yes\",", string.Empty, StringComparison.Ordinal);
        var scalarData = invalidData
            .Replace("com.kalyterasystems.opencaminterop.signal.changed.v1", "com.kalyterasystems.example.custom.v1", StringComparison.Ordinal)
            .Replace("{\"adapter\":\"test\",\"cameraId\":\"camera\",\"signal\":\"motion\",\"active\":\"yes\",\"propertyOperation\":\"changed\"}", "\"not-an-object\"", StringComparison.Ordinal);
        var missingStartedAt = """
            {
              "specversion":"1.0",
              "id":"object-1",
              "source":"urn:camera:test",
              "type":"com.kalyterasystems.opencaminterop.object.detected.v1",
              "time":"2026-09-04T10:15:30Z",
              "datacontenttype":"application/json",
              "dataschema":"urn:opencaminterop:schema:camera-object-event:1",
              "data":{"adapter":"test","cameraId":"camera","objectId":"track","label":"person","currentZones":[],"enteredZones":[]}
            }
            """;

        Assert.Throws<InvalidOperationException>(() =>
            StructuredCloudEventJson.Deserialize(Encoding.UTF8.GetBytes(invalidData)));
        Assert.Throws<InvalidOperationException>(() =>
            StructuredCloudEventJson.Deserialize(Encoding.UTF8.GetBytes(wrongSchema)));
        Assert.Throws<InvalidOperationException>(() =>
            StructuredCloudEventJson.Deserialize(Encoding.UTF8.GetBytes(missingActive)));
        Assert.Throws<InvalidOperationException>(() =>
            StructuredCloudEventJson.Deserialize(Encoding.UTF8.GetBytes(scalarData)));
        Assert.Throws<InvalidOperationException>(() =>
            StructuredCloudEventJson.Deserialize(Encoding.UTF8.GetBytes(missingStartedAt)));
    }

    [Fact]
    public void StructuredBatchEnforcesEventCountLimit()
    {
        var cloudEvent = Assert.Single(CreateFrigateAdapter().Adapt(JsonMessage(
            "frigate/events",
            FrigatePayload("new", null))).Events);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StructuredCloudEventJson.SerializeBatch(
                Enumerable.Repeat(cloudEvent, StructuredCloudEventJson.MaxBatchEvents + 1)));
    }

    [Fact]
    public void PublishedSchemasAreValidJsonWithUniqueAbsoluteIdentifiers()
    {
        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "schemas", "v1");
        var identifiers = new HashSet<Uri>();
        var files = Directory.GetFiles(schemaDirectory, "*.schema.json");

        Assert.Equal(4, files.Length);
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            Assert.Equal(
                "https://json-schema.org/draft/2020-12/schema",
                document.RootElement.GetProperty("$schema").GetString());
            var identifier = new Uri(document.RootElement.GetProperty("$id").GetString()!);
            Assert.True(identifier.IsAbsoluteUri);
            Assert.True(identifiers.Add(identifier), $"Duplicate schema identifier: {identifier}");
        }
    }

    private static FrigateEventAdapter CreateFrigateAdapter()
    {
        return new FrigateEventAdapter(new Uri("urn:camera:frigate-lab"));
    }

    private static OnvifNotificationAdapter CreateOnvifAdapter()
    {
        return new OnvifNotificationAdapter(new OnvifAdapterOptions(
            new Uri("urn:camera:onvif-lab")));
    }

    private static AdapterMessage JsonMessage(string channel, string json)
    {
        return new AdapterMessage(
            channel,
            Encoding.UTF8.GetBytes(json),
            "application/json; charset=utf-8",
            ReceivedAt);
    }

    private static AdapterMessage XmlMessage(string xml)
    {
        return new AdapterMessage(
            "pullpoint/messages",
            Encoding.UTF8.GetBytes(xml),
            "application/soap+xml; charset=utf-8",
            ReceivedAt);
    }

    private static string FrigatePayload(string nativeType, double? endTime)
    {
        var endTimeJson = endTime is null ? "null" : endTime.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return $$"""
            {
              "type": "{{nativeType}}",
              "after": {
                "id": "track-42",
                "camera": "front door",
                "label": "person",
                "start_time": 1725445800.25,
                "frame_time": 1725445802.5,
                "end_time": {{endTimeJson}},
                "score": 0.82,
                "top_score": 0.91,
                "current_zones": ["porch"],
                "entered_zones": ["driveway", "porch"],
                "sub_label": "private-person",
                "recognized_license_plate": "TEST-PLATE"
              }
            }
            """;
    }

    private static string ReadFixture(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(
            new[] { AppContext.BaseDirectory, "fixtures", "v1" }.Concat(segments).ToArray()));
    }

    private static string OnvifMotionEnvelope(
        string propertyOperation,
        string value,
        string prefix)
    {
        return OnvifEnvelope(OnvifMotionNotification(prefix, propertyOperation, value));
    }

    private static string OnvifEnvelope(string notificationMessages)
    {
        return $$"""
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:wsnt="http://docs.oasis-open.org/wsn/b-2"
                        xmlns:tt="http://www.onvif.org/ver10/schema"
                        xmlns:tns="http://www.onvif.org/ver10/topics">
              <s:Body>
                <wsnt:Notify>
                  {{notificationMessages}}
                </wsnt:Notify>
              </s:Body>
            </s:Envelope>
            """;
    }

    private static string OnvifMotionNotification(
        string prefix,
        string propertyOperation,
        string value)
    {
        return $$"""
            <wsnt:NotificationMessage xmlns:wsnt="http://docs.oasis-open.org/wsn/b-2"
                                      xmlns:tt="http://www.onvif.org/ver10/schema"
                                      xmlns:{{prefix}}="http://www.onvif.org/ver10/topics">
              <wsnt:Topic Dialect="http://www.onvif.org/ver10/tev/topicExpression/ConcreteSet">{{prefix}}:RuleEngine/CellMotionDetector/Motion</wsnt:Topic>
              <wsnt:Message>
                <tt:Message UtcTime="2026-09-04T10:15:30Z" PropertyOperation="{{propertyOperation}}">
                  <tt:Source>
                    <tt:SimpleItem Name="VideoSourceConfigurationToken" Value="source-1" />
                  </tt:Source>
                  <tt:Data>
                    <tt:SimpleItem Name="IsMotion" Value="{{value}}" />
                  </tt:Data>
                </tt:Message>
              </wsnt:Message>
            </wsnt:NotificationMessage>
            """;
    }
}
