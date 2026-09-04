using CloudNative.CloudEvents;

namespace OpenCamInterop.Adapters;

public sealed record AdapterResult(
    IReadOnlyList<CloudEvent> Events,
    IReadOnlyList<AdapterDiagnostic> Diagnostics)
{
    public bool IsSuccess => Diagnostics.All(diagnostic =>
        diagnostic.Severity != AdapterDiagnosticSeverity.Error);

    public static AdapterResult Success(params CloudEvent[] events)
    {
        return new AdapterResult(events, Array.Empty<AdapterDiagnostic>());
    }

    public static AdapterResult Failure(string code, string message, string? path = null)
    {
        return new AdapterResult(
            Array.Empty<CloudEvent>(),
            new[] { AdapterDiagnostic.Error(code, message, path) });
    }
}
