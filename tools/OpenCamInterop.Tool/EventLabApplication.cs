using System.Globalization;
using System.Text;
using System.Text.Json;
using CloudNative.CloudEvents;
using OpenCamInterop.Adapters;

namespace OpenCamInterop.EventLab;

public sealed class EventLabApplication
{
    private const string HelpText = """
        OpenCamInterop EventLab alpha

        Usage:
          opencaminterop inspect --adapter <frigate|onvif> --input <file> --source <absolute-uri> [options]
          opencaminterop verify  --manifest <manifest.json>
          opencaminterop replay  --manifest <manifest.json> [--no-wait]

        inspect options:
          --channel <name>        Input channel (defaults to frigate/events or onvif/notifications).
          --content-type <type>   Input media type (defaults to application/json or application/soap+xml).
          --received-at <time>    UTC RFC3339 delivery time (defaults to 1970-01-01T00:00:00Z).
          --topic-prefix <prefix> Frigate topic prefix (defaults to frigate).

        verify options:
          --print-matrix         Write the deterministic compatibility matrix to stdout.

        Output:
          inspect writes one structured CloudEvent, or a CloudEvents batch for multiple events, to stdout.
          replay writes deterministic structured CloudEvents NDJSON at each relative offset. Human diagnostics use stderr.

        Exit codes: 0 success, 1 unexpected failure, 2 invalid CLI/input, 3 expectation failure, 130 cancelled.
        """;

    private static readonly HashSet<string> InspectValueOptions = new(StringComparer.Ordinal)
    {
        "adapter", "input", "source", "channel", "content-type", "received-at", "topic-prefix"
    };

    private static readonly HashSet<string> ManifestValueOptions = new(StringComparer.Ordinal)
    {
        "manifest"
    };

    private static readonly HashSet<string> HelpFlag = new(StringComparer.Ordinal)
    {
        "help"
    };

    private static readonly HashSet<string> VerifyFlags = new(StringComparer.Ordinal)
    {
        "help", "print-matrix"
    };

    private static readonly HashSet<string> ReplayFlags = new(StringComparer.Ordinal)
    {
        "help", "no-wait"
    };

    private static readonly TimeSpan MaximumWaitedReplayDuration = TimeSpan.FromMinutes(10);
    private readonly TextWriter _standardOutput;
    private readonly TextWriter _standardError;
    private readonly IReplayClock _replayClock;

    public EventLabApplication(
        TextWriter standardOutput,
        TextWriter standardError,
        IReplayClock replayClock)
    {
        _standardOutput = standardOutput ?? throw new ArgumentNullException(nameof(standardOutput));
        _standardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
        _replayClock = replayClock ?? throw new ArgumentNullException(nameof(replayClock));
    }

    public static EventLabApplication CreateDefault()
    {
        return new EventLabApplication(Console.Out, Console.Error, new SystemReplayClock());
    }

    public async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        try
        {
            if (arguments.Count == 1 && arguments[0] is "help" or "--help" or "-h")
            {
                await _standardOutput.WriteLineAsync(HelpText).ConfigureAwait(false);
                return EventLabExitCodes.Success;
            }
            if (arguments.Count == 0)
                throw new EventLabInputException("cli.command", "A command is required. Use --help for usage.");

            return arguments[0] switch
            {
                "inspect" => await RunInspectAsync(arguments.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
                "verify" => await RunVerifyAsync(arguments.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
                "replay" => await RunReplayAsync(arguments.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
                _ => throw new EventLabInputException("cli.command", "The command must be inspect, verify, or replay.")
            };
        }
        catch (EventLabInputException exception)
        {
            await WriteLineAsync(_standardError, "error", exception.Code, exception.Message).ConfigureAwait(false);
            return EventLabExitCodes.InvalidInput;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteLineAsync(_standardError, "error", "cancelled", "The command was cancelled.").ConfigureAwait(false);
            return EventLabExitCodes.Cancelled;
        }
        catch (Exception)
        {
            await WriteLineAsync(
                _standardError,
                "error",
                "internal",
                "EventLab could not complete the command.").ConfigureAwait(false);
            return EventLabExitCodes.UnexpectedFailure;
        }
    }

    private async Task<int> RunInspectAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var options = CliOptions.Parse(arguments, InspectValueOptions, HelpFlag);
        if (options.Has("help"))
        {
            await _standardOutput.WriteLineAsync(HelpText).ConfigureAwait(false);
            return EventLabExitCodes.Success;
        }

        var adapter = options.Require("adapter");
        if (adapter is not ("frigate" or "onvif"))
            throw new EventLabInputException("adapter.unsupported", "The adapter must be frigate or onvif.");
        var source = ParseSource(options.Require("source"));
        var input = BoundedFileReader.Read(
            options.Require("input"),
            BoundedFileReader.AdapterPayloadLimit,
            "input");
        var channel = options.Optional("channel") ??
            (adapter == "frigate" ? "frigate/events" : "onvif/notifications");
        var contentType = options.Optional("content-type") ??
            (adapter == "frigate" ? "application/json" : "application/soap+xml");
        ValidateBoundedText(channel, 256, "cli.channel", "channel");
        ValidateBoundedText(contentType, 256, "cli.content-type", "content type");
        var receivedAt = ParseReceivedAt(options.Optional("received-at"));

        var topicPrefix = options.Optional("topic-prefix");
        if (topicPrefix is not null)
            ValidateBoundedText(topicPrefix, 249, "cli.topic-prefix", "topic prefix");
        var result = AdapterRunner.Execute(new AdapterInvocation(
            adapter,
            source,
            channel,
            contentType,
            receivedAt,
            input,
            topicPrefix));
        await WriteAdapterDiagnosticsAsync(result.Diagnostics, null).ConfigureAwait(false);
        if (!result.IsSuccess || result.Events.Count == 0)
        {
            if (result.IsSuccess)
                await WriteLineAsync(_standardError, "error", "adapter.no-events", "The adapter produced no events.").ConfigureAwait(false);
            return EventLabExitCodes.InvalidInput;
        }

        await WriteEventsAsync(result.Events, singleWhenPossible: true, cancellationToken).ConfigureAwait(false);
        return EventLabExitCodes.Success;
    }

    private async Task<int> RunVerifyAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var options = CliOptions.Parse(arguments, ManifestValueOptions, VerifyFlags);
        if (options.Has("help"))
        {
            await _standardOutput.WriteLineAsync(HelpText).ConfigureAwait(false);
            return EventLabExitCodes.Success;
        }

        var manifest = FixtureManifestLoader.Load(options.Require("manifest"));
        var failures = 0;
        foreach (var fixture in manifest.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = ReadFixturePayload(manifest, fixture);
            var invocation = AdapterRunner.FromFixture(fixture, payload);
            var result = AdapterRunner.Execute(invocation);
            var expectation = FixtureExpectation.Evaluate(fixture, result);
            if (!expectation.Matches)
            {
                failures++;
                await WriteLineAsync(_standardError, "error", expectation.Code, expectation.Message).ConfigureAwait(false);
            }

            if (fixture.ExpectedEventTypes is not null && expectation.Matches)
            {
                var repeated = AdapterRunner.Execute(invocation);
                var repeatedExpectation = FixtureExpectation.Evaluate(fixture, repeated);
                if (!repeatedExpectation.Matches)
                {
                    failures++;
                    await WriteLineAsync(
                        _standardError,
                        "error",
                        repeatedExpectation.Code,
                        repeatedExpectation.Message).ConfigureAwait(false);
                }
                else if (!EventIdentities(result.Events).SequenceEqual(EventIdentities(repeated.Events)))
                {
                    failures++;
                    await WriteLineAsync(
                        _standardError,
                        "error",
                        "expectation.identity",
                        $"case {fixture.Id}: repeated adaptation changed a CloudEvent source/id pair.").ConfigureAwait(false);
                }
            }
        }

        if (failures > 0)
            return EventLabExitCodes.ExpectationFailed;

        if (options.Has("print-matrix"))
        {
            await _standardOutput.WriteAsync(CompatibilityMatrix.Render(manifest)).ConfigureAwait(false);
        }
        else if (!CompatibilityMatrix.IsCurrent(manifest))
        {
            await WriteLineAsync(
                _standardError,
                "error",
                "expectation.matrix",
                "COMPATIBILITY.md does not match the fixture manifest; regenerate it with verify --print-matrix.").ConfigureAwait(false);
            return EventLabExitCodes.ExpectationFailed;
        }

        await WriteLineAsync(
            _standardError,
            "ok",
            "verify",
            $"{manifest.Cases.Count.ToString(CultureInfo.InvariantCulture)} fixture cases matched.").ConfigureAwait(false);
        return EventLabExitCodes.Success;
    }

    private async Task<int> RunReplayAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var options = CliOptions.Parse(arguments, ManifestValueOptions, ReplayFlags);
        if (options.Has("help"))
        {
            await _standardOutput.WriteLineAsync(HelpText).ConfigureAwait(false);
            return EventLabExitCodes.Success;
        }

        var manifest = FixtureManifestLoader.Load(options.Require("manifest"));
        var noWait = options.Has("no-wait");
        ValidateReplayTimeline(manifest, noWait);
        var prepared = new List<PreparedReplayCase>(manifest.Cases.Count);
        var eventCount = 0;
        var encodedBytes = 0;
        foreach (var fixture in manifest.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = ExecuteFixture(manifest, fixture);
            var expectation = FixtureExpectation.Evaluate(fixture, result);
            if (!expectation.Matches)
            {
                await WriteLineAsync(_standardError, "error", expectation.Code, expectation.Message).ConfigureAwait(false);
                return EventLabExitCodes.ExpectationFailed;
            }

            var jsonEvents = new List<string>(result.Events.Count);
            foreach (var cloudEvent in result.Events)
            {
                var encoded = StructuredCloudEventJson.Serialize(cloudEvent);
                encodedBytes = checked(encodedBytes + encoded.Length + 1);
                if (encodedBytes > StructuredCloudEventJson.MaxEncodedBytes)
                {
                    throw new EventLabInputException(
                        "replay.output-limit",
                        $"Replay output cannot exceed {StructuredCloudEventJson.MaxEncodedBytes} bytes.");
                }
                jsonEvents.Add(Encoding.UTF8.GetString(encoded.Span));
            }

            eventCount = checked(eventCount + result.Events.Count);
            prepared.Add(new PreparedReplayCase(fixture, jsonEvents, result.Diagnostics));
        }

        DateTimeOffset? previous = null;
        foreach (var item in prepared)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!noWait && previous.HasValue)
            {
                var delay = item.Fixture.ReceivedAt - previous.Value;
                await _replayClock.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            previous = item.Fixture.ReceivedAt;

            foreach (var diagnostic in item.Diagnostics.Where(diagnostic =>
                         diagnostic.Severity == AdapterDiagnosticSeverity.Warning))
            {
                await WriteAdapterDiagnosticAsync(diagnostic, item.Fixture.Id).ConfigureAwait(false);
            }
            foreach (var json in item.JsonEvents)
                await _standardOutput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await WriteLineAsync(
            _standardError,
            "ok",
            "replay",
            $"{manifest.Cases.Count.ToString(CultureInfo.InvariantCulture)} cases produced {eventCount.ToString(CultureInfo.InvariantCulture)} events.").ConfigureAwait(false);
        return EventLabExitCodes.Success;
    }

    private static AdapterResult ExecuteFixture(FixtureManifest manifest, FixtureCase fixture)
    {
        var payload = ReadFixturePayload(manifest, fixture);
        return AdapterRunner.Execute(AdapterRunner.FromFixture(fixture, payload));
    }

    private static byte[] ReadFixturePayload(FixtureManifest manifest, FixtureCase fixture)
    {
        return BoundedFileReader.Read(fixture.PayloadPath, manifest.MaximumPayloadBytes, "fixture");
    }

    private static IEnumerable<(string Source, string Id)> EventIdentities(IEnumerable<CloudEvent> events)
    {
        return events.Select(item => (item.Source!.OriginalString, item.Id!));
    }

    private static void ValidateReplayTimeline(FixtureManifest manifest, bool noWait)
    {
        var totalDelay = TimeSpan.Zero;
        DateTimeOffset? previous = null;
        foreach (var fixture in manifest.Cases)
        {
            if (previous.HasValue)
            {
                var delay = fixture.ReceivedAt - previous.Value;
                if (delay < TimeSpan.Zero)
                    throw new EventLabInputException("replay.time-order", "Fixture receivedAt values must be nondecreasing for replay.");
                totalDelay += delay;
            }
            previous = fixture.ReceivedAt;
        }

        if (!noWait && totalDelay > MaximumWaitedReplayDuration)
        {
            throw new EventLabInputException(
                "replay.duration",
                "Timed replay is limited to ten minutes; use --no-wait for longer traces.");
        }
    }

    private static Uri ParseSource(string value)
    {
        if (value.Length > 2048 ||
            value.Any(char.IsControl) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var source) ||
            !string.IsNullOrEmpty(source.UserInfo) ||
            !string.IsNullOrEmpty(source.Query) ||
            !string.IsNullOrEmpty(source.Fragment))
        {
            throw new EventLabInputException(
                "cli.source",
                "The source must be a safe absolute URI of at most 2048 characters.");
        }

        return source;
    }

    private static DateTimeOffset ParseReceivedAt(string? value)
    {
        if (value is null)
            return DateTimeOffset.UnixEpoch;

        var timeSeparator = value.IndexOf('T');
        var hasExplicitZone = value.EndsWith('Z') || value.EndsWith('z') ||
            (timeSeparator >= 0 &&
             (value.LastIndexOf('+') > timeSeparator || value.LastIndexOf('-') > timeSeparator));
        DateTimeOffset timestamp;
        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
            if (!hasExplicitZone ||
                !document.RootElement.TryGetDateTimeOffset(out timestamp) ||
                timestamp.Offset != TimeSpan.Zero)
            {
                throw new EventLabInputException(
                    "cli.received-at",
                    "The received-at value must be a UTC RFC3339 timestamp.");
            }
        }
        catch (JsonException)
        {
            throw new EventLabInputException("cli.received-at", "The received-at value must be a UTC RFC3339 timestamp.");
        }

        return timestamp;
    }

    private static void ValidateBoundedText(string value, int maximumLength, string code, string description)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
            throw new EventLabInputException(code, $"The {description} is invalid.");
    }

    private async Task WriteEventsAsync(
        IReadOnlyList<CloudEvent> events,
        bool singleWhenPossible,
        CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> encoded = singleWhenPossible && events.Count == 1
            ? StructuredCloudEventJson.Serialize(events[0])
            : StructuredCloudEventJson.SerializeBatch(events);
        var json = Encoding.UTF8.GetString(encoded.Span);
        await _standardOutput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAdapterDiagnosticsAsync(
        IEnumerable<AdapterDiagnostic> diagnostics,
        string? fixtureId)
    {
        foreach (var diagnostic in diagnostics)
            await WriteAdapterDiagnosticAsync(diagnostic, fixtureId).ConfigureAwait(false);
    }

    private Task WriteAdapterDiagnosticAsync(AdapterDiagnostic diagnostic, string? fixtureId)
    {
        var severity = diagnostic.Severity == AdapterDiagnosticSeverity.Error ? "error" : "warning";
        var context = fixtureId is null ? string.Empty : $"case {fixtureId}: ";
        var path = diagnostic.Path is null ? string.Empty : $" at {Sanitize(diagnostic.Path)}";
        return WriteLineAsync(
            _standardError,
            severity,
            diagnostic.Code,
            $"{context}{Sanitize(diagnostic.Message)}{path}");
    }

    private static async Task WriteLineAsync(
        TextWriter writer,
        string level,
        string code,
        string message)
    {
        await writer.WriteLineAsync($"{level} [{Sanitize(code)}]: {Sanitize(message)}").ConfigureAwait(false);
    }

    private static string Sanitize(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }

    private sealed record PreparedReplayCase(
        FixtureCase Fixture,
        IReadOnlyList<string> JsonEvents,
        IReadOnlyList<AdapterDiagnostic> Diagnostics);
}
