using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenCamInterop;

public static class CameraEventId
{
    public static string FromPayload(string adapterName, ReadOnlySpan<byte> payload)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
            throw new ArgumentException("An adapter name is required.", nameof(adapterName));

        var normalizedName = NormalizeAdapterName(adapterName);
        var hash = SHA256.HashData(payload);
        return $"{normalizedName}-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static string FromAdapterMessage(
        string adapterName,
        string channel,
        ReadOnlySpan<byte> payload)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
            throw new ArgumentException("An adapter name is required.", nameof(adapterName));
        ArgumentNullException.ThrowIfNull(channel);
        var normalizedName = NormalizeAdapterName(adapterName);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, Encoding.UTF8.GetBytes(adapterName));
        AppendField(hash, Encoding.UTF8.GetBytes(channel));
        AppendField(hash, payload);
        return $"{normalizedName}-{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    public static string FromText(string adapterName, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return FromPayload(adapterName, Encoding.UTF8.GetBytes(value));
    }

    private static string NormalizeAdapterName(string adapterName)
    {
        var normalized = new string(adapterName
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray());

        return normalized.Trim('-') is { Length: > 0 } result ? result : "event";
    }

    private static void AppendField(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}
