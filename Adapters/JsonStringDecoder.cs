using System.Text.Json;

namespace OpenCamInterop.Adapters;

internal static class JsonStringDecoder
{
    internal static string? GetString(JsonElement element)
    {
        // Keep incorrect API usage distinct from malformed caller-supplied text.
        if (element.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("A JSON string value is required.");

        try
        {
            return element.GetString();
        }
        catch (InvalidOperationException exception) when (exception is not ObjectDisposedException)
        {
            // JsonDocument defers Unicode decoding until a string is accessed.
            throw new JsonException("A JSON string contains invalid Unicode.");
        }
    }

    internal static string GetPropertyName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException exception) when (exception is not ObjectDisposedException)
        {
            throw new JsonException("A JSON property name contains invalid Unicode.");
        }
    }
}
