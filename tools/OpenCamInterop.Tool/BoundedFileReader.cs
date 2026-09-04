namespace OpenCamInterop.EventLab;

internal static class BoundedFileReader
{
    internal const int AdapterPayloadLimit = 1024 * 1024;
    internal const int ManifestLimit = 64 * 1024;
    internal const int MaximumPathLength = 4096;

    internal static byte[] Read(string path, int maximumBytes, string kind)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumPathLength || path.Any(char.IsControl))
            throw new EventLabInputException($"{kind}.path-invalid", $"The {kind} path is invalid.");

        try
        {
            var fullPath = Path.GetFullPath(path);
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            if (stream.Length > maximumBytes)
                throw new EventLabInputException($"{kind}.too-large", $"The {kind} exceeds the {maximumBytes}-byte limit.");

            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1)
                throw new EventLabInputException($"{kind}.changed", $"The {kind} changed while it was being read.");
            return bytes;
        }
        catch (EventLabInputException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            throw new EventLabInputException($"{kind}.unreadable", $"The {kind} could not be read.");
        }
    }
}
