using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenCamInterop.EventLab;

namespace OpenCamInterop.Tool.Tests;

public sealed class EventLabApplicationTests
{
    [Fact]
    public async Task InspectWritesOnlyAStructuredCloudEventToStandardOutput()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(output, error, new RecordingReplayClock());

        var exitCode = await application.RunAsync(new[]
        {
            "inspect",
            "--adapter", "frigate",
            "--input", FixturePath("frigate", "object-new.json"),
            "--source", "urn:opencaminterop:test:inspect"
        });

        Assert.Equal(EventLabExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        var cloudEvent = StructuredCloudEventJson.Deserialize(Encoding.UTF8.GetBytes(output.ToString()));
        Assert.Equal(CameraEventTypes.ObjectDetected, cloudEvent.Type);
    }

    [Fact]
    public async Task VerifyAcceptsTheVersionedFixtureManifest()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(output, error, new RecordingReplayClock());

        var exitCode = await application.RunAsync(new[]
        {
            "verify", "--manifest", FixturePath("manifest.json")
        });

        Assert.Equal(EventLabExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("ok [verify]: 4 fixture cases matched.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyPrintsTheCheckedInCompatibilityMatrixExactly()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(output, error, new RecordingReplayClock());

        var exitCode = await application.RunAsync(new[]
        {
            "verify", "--manifest", FixturePath("manifest.json"), "--print-matrix"
        });

        Assert.Equal(EventLabExitCodes.Success, exitCode);
        var expected = File.ReadAllText(FixturePath("COMPATIBILITY.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, output.ToString());
        Assert.Contains("ok [verify]", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyCanReproduceTheCompatibilityMatrixOnStandardOutput()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(output, error, new RecordingReplayClock());

        var exitCode = await application.RunAsync(new[]
        {
            "verify", "--manifest", FixturePath("manifest.json"), "--print-matrix"
        });

        Assert.Equal(EventLabExitCodes.Success, exitCode);
        Assert.Equal(
            File.ReadAllText(FixturePath("COMPATIBILITY.md")).Replace("\r\n", "\n", StringComparison.Ordinal),
            output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains("ok [verify]", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAdapterFailuresUseInvalidInputAndLeaveStandardOutputEmpty()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(output, error, new RecordingReplayClock());

        var exitCode = await application.RunAsync(new[]
        {
            "inspect",
            "--adapter", "frigate",
            "--input", FixturePath("frigate", "object-new.json"),
            "--source", "urn:opencaminterop:test:inspect",
            "--channel", "other/events"
        });

        Assert.Equal(EventLabExitCodes.InvalidInput, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("error [frigate.channel.unsupported]", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyUsesADistinctExpectationFailureExitCode()
    {
        using var corpus = TemporaryCorpus.Create(CameraEventTypes.ObjectUpdated);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(output, error, new RecordingReplayClock());

        var exitCode = await application.RunAsync(new[]
        {
            "verify", "--manifest", corpus.ManifestPath
        });

        Assert.Equal(EventLabExitCodes.ExpectationFailed, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("error [expectation.events]", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayPreservesManifestOrderAndUsesTheInjectedClock()
    {
        using var corpus = TemporaryCorpus.Create(
            CameraEventTypes.ObjectDetected,
            secondEventType: CameraEventTypes.ObjectUpdated,
            secondOffset: TimeSpan.FromMilliseconds(1250));
        var clock = new RecordingReplayClock();
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(output, error, clock);

        var exitCode = await application.RunAsync(new[]
        {
            "replay", "--manifest", corpus.ManifestPath
        });

        Assert.Equal(EventLabExitCodes.Success, exitCode);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(1250) }, clock.Delays);
        var events = output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(json => StructuredCloudEventJson.Deserialize(Encoding.UTF8.GetBytes(json)))
            .ToArray();
        Assert.Equal(
            new[] { CameraEventTypes.ObjectDetected, CameraEventTypes.ObjectUpdated },
            events.Select(item => item.Type));
    }

    [Fact]
    public async Task ReplayNoWaitDoesNotCallTheClock()
    {
        using var corpus = TemporaryCorpus.Create(
            CameraEventTypes.ObjectDetected,
            secondEventType: CameraEventTypes.ObjectUpdated,
            secondOffset: TimeSpan.FromHours(2));
        var clock = new RecordingReplayClock();
        var application = new EventLabApplication(
            new StringWriter(CultureInfo.InvariantCulture),
            new StringWriter(CultureInfo.InvariantCulture),
            clock);

        var exitCode = await application.RunAsync(new[]
        {
            "replay", "--manifest", corpus.ManifestPath, "--no-wait"
        });

        Assert.Equal(EventLabExitCodes.Success, exitCode);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task ManifestTraversalIsAnInvalidInputWithoutStandardOutput()
    {
        using var corpus = TemporaryCorpus.Create(
            CameraEventTypes.ObjectDetected,
            payloadPath: "frigate/../outside.json",
            createPayload: false);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(output, error, new RecordingReplayClock());

        var exitCode = await application.RunAsync(new[]
        {
            "verify", "--manifest", corpus.ManifestPath
        });

        Assert.Equal(EventLabExitCodes.InvalidInput, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("error [manifest.payload-path]", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManifestRejectsDuplicateProperties()
    {
        using var corpus = TemporaryCorpus.Create(CameraEventTypes.ObjectDetected);
        corpus.RewriteManifest(json => json.Replace(
            "\"schemaVersion\":1,",
            "\"schemaVersion\":1,\"schemaVersion\":1,",
            StringComparison.Ordinal));

        var result = await VerifyAsync(corpus.ManifestPath);

        Assert.Equal(EventLabExitCodes.InvalidInput, result.ExitCode);
        Assert.Contains("error [manifest.duplicate-property]", result.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public async Task ManifestRejectsDuplicateIdsAndUnsupportedAdapters()
    {
        using var duplicateIds = TemporaryCorpus.Create(
            CameraEventTypes.ObjectDetected,
            secondEventType: CameraEventTypes.ObjectUpdated);
        duplicateIds.RewriteManifest(json => json.Replace(
            "\"id\":\"second\"",
            "\"id\":\"first\"",
            StringComparison.Ordinal));

        var duplicateResult = await VerifyAsync(duplicateIds.ManifestPath);

        Assert.Equal(EventLabExitCodes.InvalidInput, duplicateResult.ExitCode);
        Assert.Contains("error [manifest.case-id]", duplicateResult.Error, StringComparison.Ordinal);

        using var unsupported = TemporaryCorpus.Create(CameraEventTypes.ObjectDetected);
        unsupported.RewriteManifest(json => json.Replace(
            "\"adapter\":\"frigate\"",
            "\"adapter\":\"unsupported\"",
            StringComparison.Ordinal));

        var unsupportedResult = await VerifyAsync(unsupported.ManifestPath);

        Assert.Equal(EventLabExitCodes.InvalidInput, unsupportedResult.ExitCode);
        Assert.Contains("error [manifest.adapter]", unsupportedResult.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManifestRejectsMissingOversizedAndUnmanifestedPayloads()
    {
        using var missing = TemporaryCorpus.Create(
            CameraEventTypes.ObjectDetected,
            payloadPath: "frigate/missing.json",
            createPayload: false);

        var missingResult = await VerifyAsync(missing.ManifestPath);

        Assert.Equal(EventLabExitCodes.InvalidInput, missingResult.ExitCode);
        Assert.Contains("error [fixture.unreadable]", missingResult.Error, StringComparison.Ordinal);

        using var oversized = TemporaryCorpus.Create(CameraEventTypes.ObjectDetected);
        File.WriteAllBytes(oversized.PayloadPath("frigate", "first.json"), new byte[(1024 * 1024) + 1]);

        var oversizedResult = await VerifyAsync(oversized.ManifestPath);

        Assert.Equal(EventLabExitCodes.InvalidInput, oversizedResult.ExitCode);
        Assert.Contains("error [fixture.too-large]", oversizedResult.Error, StringComparison.Ordinal);

        using var orphaned = TemporaryCorpus.Create(CameraEventTypes.ObjectDetected);
        File.WriteAllText(orphaned.PayloadPath("frigate", "orphan.json"), "{}");

        var orphanedResult = await VerifyAsync(orphaned.ManifestPath);

        Assert.Equal(EventLabExitCodes.InvalidInput, orphanedResult.ExitCode);
        Assert.Contains("error [manifest.coverage]", orphanedResult.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayReturnsCancellationCodeWithoutPartialOutput()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(output, error, new RecordingReplayClock());

        var exitCode = await application.RunAsync(
            new[] { "replay", "--manifest", FixturePath("manifest.json"), "--no-wait" },
            cancellation.Token);

        Assert.Equal(EventLabExitCodes.Cancelled, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("error [cancelled]", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckedInCorpusKeepsStableEventIdentities()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(
            output,
            new StringWriter(CultureInfo.InvariantCulture),
            new RecordingReplayClock());

        var exitCode = await application.RunAsync(new[]
        {
            "replay", "--manifest", FixturePath("manifest.json"), "--no-wait"
        });

        Assert.Equal(EventLabExitCodes.Success, exitCode);
        var ids = output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(json => StructuredCloudEventJson.Deserialize(Encoding.UTF8.GetBytes(json)).Id)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "frigate-events-v1-7289edc029e95d792deb8993c995018c05c5102cc8c934d6c62dbbe1c9b614f7",
                "onvif-notification-v1-182c08ec85c891b61a028bacee7dae1d1b4caf823fa251515dfef0dbd7b9b0a9",
                "onvif-notification-v1-fd238d7f1bb29de145b48a561f8d2dcbea736a8ea0159d581233350616369115"
            },
            ids);
    }

    private static async Task<(int ExitCode, string Output, string Error)> VerifyAsync(string manifestPath)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var application = new EventLabApplication(output, error, new RecordingReplayClock());
        var exitCode = await application.RunAsync(new[] { "verify", "--manifest", manifestPath });
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string FixturePath(params string[] segments)
    {
        return Path.Combine(new[] { AppContext.BaseDirectory, "fixtures", "v1" }.Concat(segments).ToArray());
    }

    private sealed class RecordingReplayClock : IReplayClock
    {
        internal List<TimeSpan> Delays { get; } = new();

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryCorpus : IDisposable
    {
        private TemporaryCorpus(string directory, string manifestPath)
        {
            Directory = directory;
            ManifestPath = manifestPath;
        }

        internal string Directory { get; }

        internal string ManifestPath { get; }

        internal string PayloadPath(params string[] segments)
        {
            return Path.Combine(new[] { Directory }.Concat(segments).ToArray());
        }

        internal void RewriteManifest(Func<string, string> transform)
        {
            File.WriteAllText(ManifestPath, transform(File.ReadAllText(ManifestPath)));
        }

        internal static TemporaryCorpus Create(
            string firstEventType,
            string? secondEventType = null,
            TimeSpan? secondOffset = null,
            string payloadPath = "frigate/first.json",
            bool createPayload = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"opencaminterop-tool-tests-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var firstPayload = FrigatePayload("new");
            if (createPayload)
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(directory, payloadPath))!);
                File.WriteAllText(Path.Combine(directory, payloadPath), firstPayload);
            }

            var receivedAt = new DateTimeOffset(2026, 9, 4, 10, 30, 0, TimeSpan.Zero);
            var cases = new List<object>
            {
                FixtureCase("first", payloadPath, receivedAt, firstEventType)
            };
            if (secondEventType is not null)
            {
                const string secondPayloadPath = "frigate/second.json";
                System.IO.Directory.CreateDirectory(Path.Combine(directory, "frigate"));
                File.WriteAllText(Path.Combine(directory, secondPayloadPath), FrigatePayload("update"));
                cases.Add(FixtureCase(
                    "second",
                    secondPayloadPath,
                    receivedAt + (secondOffset ?? TimeSpan.Zero),
                    secondEventType));
            }

            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifest = new
            {
                schemaVersion = 1,
                maxPayloadBytes = 1024 * 1024,
                cases
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
            return new TemporaryCorpus(directory, manifestPath);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }

        private static object FixtureCase(
            string id,
            string payload,
            DateTimeOffset receivedAt,
            string expectedEventType)
        {
            return new
            {
                id,
                adapter = "frigate",
                payload,
                source = "urn:opencaminterop:test:fixture",
                channel = "frigate/events",
                contentType = "application/json",
                receivedAt = receivedAt.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                    CultureInfo.InvariantCulture),
                expectedEventTypes = new[] { expectedEventType },
                note = "Synthetic EventLab test fixture."
            };
        }

        private static string FrigatePayload(string type)
        {
            return $$"""
                {
                  "type": "{{type}}",
                  "after": {
                    "id": "synthetic-object",
                    "camera": "synthetic-camera",
                    "label": "person",
                    "start_time": 1725445800.25,
                    "frame_time": 1725445802.5,
                    "current_zones": [],
                    "entered_zones": []
                  }
                }
                """;
        }
    }
}
