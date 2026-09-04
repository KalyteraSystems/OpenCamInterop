namespace OpenCamInterop.Adapters;

internal static class AdapterPayloadGuard
{
    internal const int MaxPayloadBytes = 1024 * 1024;

    internal static AdapterDiagnostic? Validate(AdapterMessage message)
    {
        if (message.Payload.IsEmpty)
            return AdapterDiagnostic.Error("payload.empty", "The adapter payload is empty.");
        if (message.Payload.Length > MaxPayloadBytes)
        {
            return AdapterDiagnostic.Error(
                "payload.too-large",
                "The adapter payload exceeds the 1 MiB limit.");
        }

        return null;
    }
}
