using CloudNative.CloudEvents;
using OpenCamInterop.Adapters;
using OpenCamInterop.Adapters.Frigate;
using OpenCamInterop.Adapters.Onvif;

namespace OpenCamInterop.EventLab;

internal sealed record AdapterInvocation(
    string Adapter,
    Uri Source,
    string Channel,
    string ContentType,
    DateTimeOffset ReceivedAt,
    ReadOnlyMemory<byte> Payload,
    string? TopicPrefix = null);

internal static class AdapterRunner
{
    internal static AdapterResult Execute(AdapterInvocation invocation)
    {
        ICameraEventAdapter adapter;
        try
        {
            adapter = invocation.Adapter switch
            {
                "frigate" => new FrigateEventAdapter(new FrigateAdapterOptions(
                    invocation.Source,
                    invocation.TopicPrefix ?? "frigate")),
                "onvif" when invocation.TopicPrefix is null =>
                    new OnvifNotificationAdapter(new OnvifAdapterOptions(invocation.Source)),
                "onvif" => throw new EventLabInputException(
                    "cli.option-incompatible",
                    "The --topic-prefix option is supported only by the Frigate adapter."),
                _ => throw new EventLabInputException(
                    "adapter.unsupported",
                    "The adapter must be frigate or onvif.")
            };
        }
        catch (EventLabInputException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw new EventLabInputException("adapter.options", "The adapter options are invalid.");
        }

        return adapter.Adapt(new AdapterMessage(
            invocation.Channel,
            invocation.Payload,
            invocation.ContentType,
            invocation.ReceivedAt));
    }

    internal static AdapterInvocation FromFixture(FixtureCase fixture, ReadOnlyMemory<byte> payload)
    {
        return new AdapterInvocation(
            fixture.Adapter,
            fixture.Source,
            fixture.Channel,
            fixture.ContentType,
            fixture.ReceivedAt,
            payload);
    }
}

internal static class FixtureExpectation
{
    internal static ExpectationResult Evaluate(FixtureCase fixture, AdapterResult result)
    {
        var errorCodes = result.Diagnostics
            .Where(item => item.Severity == AdapterDiagnosticSeverity.Error)
            .Select(item => item.Code)
            .ToArray();

        if (fixture.ExpectedDiagnosticCode is not null)
        {
            var matches = result.Events.Count == 0 &&
                errorCodes.Length == 1 &&
                string.Equals(errorCodes[0], fixture.ExpectedDiagnosticCode, StringComparison.Ordinal);
            return matches
                ? ExpectationResult.Success
                : new ExpectationResult(
                    false,
                    "expectation.diagnostic",
                    $"case {fixture.Id}: expected diagnostic {fixture.ExpectedDiagnosticCode}; received {Describe(errorCodes)}.");
        }

        var actualTypes = result.Events.Select(item => item.Type ?? "<missing>").ToArray();
        var typesMatch = fixture.ExpectedEventTypes!.SequenceEqual(actualTypes, StringComparer.Ordinal);
        if (errorCodes.Length == 0 && typesMatch)
            return ExpectationResult.Success;

        return new ExpectationResult(
            false,
            "expectation.events",
            $"case {fixture.Id}: expected events {Describe(fixture.ExpectedEventTypes!)}; received {Describe(actualTypes)} with errors {Describe(errorCodes)}.");
    }

    private static string Describe(IEnumerable<string> values)
    {
        var materialized = values.ToArray();
        return materialized.Length == 0 ? "[]" : $"[{string.Join(',', materialized)}]";
    }
}

internal sealed record ExpectationResult(bool Matches, string Code, string Message)
{
    internal static ExpectationResult Success { get; } = new(true, string.Empty, string.Empty);
}
