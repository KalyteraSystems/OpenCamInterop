using System.Text;
using OpenCamInterop.Adapters;
using OpenCamInterop.Adapters.Frigate;
using OpenCamInterop.Adapters.Scrypted;

namespace OpenCamInterop.Tests;

public sealed class JsonAdapterUnicodeTests
{
    public static IEnumerable<object[]> InvalidEscapedStrings()
    {
        foreach (var (adapter, fields) in new[]
                 {
                     ("scrypted", new[] { "id", "className", "zones", "root-property", "entry-property" }),
                     ("frigate", new[] { "id", "camera", "label", "current_zones", "entered_zones", "root-property", "entry-property" })
                 })
        {
            foreach (var field in fields)
            {
                foreach (var escaped in new[] { @"\uD800", @"\uDC00", @"\uD800x", @"\uD800\uD800" })
                    yield return new object[] { adapter, field, escaped };
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidEscapedStrings))]
    public void InvalidEscapedUnicodeReturnsOnlyStableJsonDiagnostic(string adapter, string field, string escaped)
    {
        var payload = Payload(adapter, field, "synthetic-private-" + escaped);

        var result = Adapt(adapter, Encoding.UTF8.GetBytes(payload));

        AssertInvalidJson(adapter, result);
    }

    [Theory]
    [InlineData("scrypted", "id")]
    [InlineData("scrypted", "root-property")]
    [InlineData("frigate", "id")]
    [InlineData("frigate", "root-property")]
    public void InvalidUtf8ReturnsOnlyStableJsonDiagnostic(string adapter, string field)
    {
        const string marker = "INVALID_UTF8";
        var payload = Payload(adapter, field, marker);
        var markerIndex = payload.IndexOf(marker, StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(payload[..markerIndex])
            .Concat(new byte[] { 0xed, 0xa0, 0x80 })
            .Concat(Encoding.UTF8.GetBytes(payload[(markerIndex + marker.Length)..]))
            .ToArray();

        var result = Adapt(adapter, bytes);

        AssertInvalidJson(adapter, result);
    }

    [Theory]
    [InlineData("scrypted")]
    [InlineData("frigate")]
    public void ValidSurrogatePairsRoundTripThroughIdentifiersZonesAndPropertyNames(string adapter)
    {
        const string escaped = @"synthetic-\uD83D\uDE00";
        var payload = Payload(adapter, "id", escaped)
            .Replace("synthetic-zone", escaped, StringComparison.Ordinal)
            .Replace("rootExtra", escaped, StringComparison.Ordinal)
            .Replace("entryExtra", escaped, StringComparison.Ordinal);

        var result = Adapt(adapter, Encoding.UTF8.GetBytes(payload));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        var cloudEvent = Assert.Single(result.Events);
        Assert.EndsWith("/objects/synthetic-%F0%9F%98%80", cloudEvent.Subject, StringComparison.Ordinal);
        if (adapter == "scrypted")
        {
            var data = Assert.IsType<CameraObjectObservationEventData>(cloudEvent.Data);
            Assert.Equal("synthetic-😀", data.ObjectId);
            Assert.Equal(new[] { "synthetic-😀" }, data.Zones);
        }
        else
        {
            var data = Assert.IsType<CameraObjectEventData>(cloudEvent.Data);
            Assert.Equal("synthetic-😀", data.ObjectId);
            Assert.Equal(new[] { "synthetic-😀" }, data.CurrentZones);
            Assert.Equal(new[] { "synthetic-😀" }, data.EnteredZones);
        }
        Assert.NotEmpty(StructuredCloudEventJson.Serialize(cloudEvent).ToArray());
    }

    [Theory]
    [InlineData("scrypted")]
    [InlineData("frigate")]
    public void UnicodeDecodingPreservesIdentifierAndPayloadLimits(string adapter)
    {
        var maximumIdentifier = string.Concat(Enumerable.Repeat(@"\uD83D\uDE00", 256));
        var accepted = Adapt(adapter, Encoding.UTF8.GetBytes(Payload(adapter, "id", maximumIdentifier)));
        var rejected = Adapt(adapter, Encoding.UTF8.GetBytes(Payload(adapter, "id", maximumIdentifier + "x")));
        var oversized = Adapt(adapter, new byte[(1024 * 1024) + 1]);

        Assert.True(accepted.IsSuccess);
        Assert.Single(accepted.Events);
        Assert.Empty(rejected.Events);
        Assert.Equal(adapter + ".field.invalid", Assert.Single(rejected.Diagnostics).Code);
        Assert.Equal("payload.too-large", Assert.Single(oversized.Diagnostics).Code);
    }

    private static void AssertInvalidJson(string adapter, AdapterResult result)
    {
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Events);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(adapter + ".json.invalid", diagnostic.Code);
        Assert.Equal("$", diagnostic.Path);
        Assert.Equal(AdapterDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(adapter == "scrypted"
            ? "The Scrypted ObjectsDetected payload is not valid JSON."
            : "The Frigate events payload is not valid JSON.", diagnostic.Message);
    }

    private static AdapterResult Adapt(string adapter, byte[] payload)
    {
        ICameraEventAdapter instance = adapter == "scrypted"
            ? new ScryptedObjectsDetectedAdapter(new ScryptedAdapterOptions(new Uri("urn:camera:synthetic"), "synthetic-camera"))
            : new FrigateEventAdapter(new Uri("urn:camera:synthetic"));
        return instance.Adapt(new AdapterMessage(
            adapter == "scrypted" ? "ObjectDetector" : "frigate/events",
            payload,
            "application/json",
            new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero)));
    }

    private static string Payload(string adapter, string field, string value)
    {
        var payload = adapter == "scrypted"
            ? """{"timestamp":1788807600000,"detections":[{"id":"synthetic-id","className":"person","score":0.9,"zones":["synthetic-zone"],"entryExtra":0}],"rootExtra":0}"""
            : """{"type":"new","after":{"id":"synthetic-id","camera":"synthetic-camera","label":"person","start_time":1788807600,"current_zones":["synthetic-zone"],"entered_zones":["synthetic-zone"],"entryExtra":0},"rootExtra":0}""";
        return field switch
        {
            "root-property" => payload.Replace("rootExtra", value, StringComparison.Ordinal),
            "entry-property" => payload.Replace("entryExtra", value, StringComparison.Ordinal),
            "zones" or "current_zones" or "entered_zones" => payload.Replace(
                $"\"{field}\":[\"synthetic-zone\"]", $"\"{field}\":[\"{value}\"]", StringComparison.Ordinal),
            _ => payload.Replace(
                $"\"{field}\":\"{(field == "id" ? "synthetic-id" : field == "camera" ? "synthetic-camera" : "person")}\"",
                $"\"{field}\":\"{value}\"", StringComparison.Ordinal)
        };
    }
}
