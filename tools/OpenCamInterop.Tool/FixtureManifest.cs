using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCamInterop.EventLab;

internal sealed record FixtureManifest(
    string Path,
    string Directory,
    int MaximumPayloadBytes,
    IReadOnlyList<FixtureCase> Cases);

internal sealed record FixtureCase(
    string Id,
    string Adapter,
    string Payload,
    string PayloadPath,
    Uri Source,
    string Channel,
    string ContentType,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<string>? ExpectedEventTypes,
    string? ExpectedDiagnosticCode,
    string Note);

internal static class FixtureManifestLoader
{
    internal const int SchemaVersion = 1;
    internal const int MaximumCases = 256;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly HashSet<string> KnownEventTypes = new(StringComparer.Ordinal)
    {
        CameraEventTypes.ObjectDetected,
        CameraEventTypes.ObjectUpdated,
        CameraEventTypes.ObjectEnded,
        CameraEventTypes.SignalChanged,
        CameraEventTypes.OnvifNotification
    };

    internal static FixtureManifest Load(string path)
    {
        var bytes = BoundedFileReader.Read(path, BoundedFileReader.ManifestLimit, "manifest");
        if (bytes.Length == 0)
            throw new EventLabInputException("manifest.empty", "The fixture manifest is empty.");

        ManifestDocument? document;
        try
        {
            using var parsed = JsonDocument.Parse(bytes, DocumentOptions);
            EnsureNoDuplicateProperties(parsed.RootElement);
            ValidateRequiredJsonShape(parsed.RootElement);
            document = parsed.RootElement.Deserialize<ManifestDocument>(SerializerOptions);
        }
        catch (EventLabInputException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new EventLabInputException("manifest.invalid", "The fixture manifest is not valid v1 JSON.");
        }

        if (document is null)
            throw new EventLabInputException("manifest.invalid", "The fixture manifest is not valid v1 JSON.");
        if (document.SchemaVersion != SchemaVersion)
            throw new EventLabInputException("manifest.version", $"The fixture manifest schemaVersion must be {SchemaVersion}.");
        if (document.MaxPayloadBytes != BoundedFileReader.AdapterPayloadLimit)
        {
            throw new EventLabInputException(
                "manifest.payload-limit",
                $"The fixture manifest maxPayloadBytes must be {BoundedFileReader.AdapterPayloadLimit}.");
        }
        if (document.Cases is null || document.Cases.Count == 0 || document.Cases.Count > MaximumCases)
        {
            throw new EventLabInputException(
                "manifest.case-count",
                $"The fixture manifest must contain from 1 through {MaximumCases} cases.");
        }

        var manifestPath = Path.GetFullPath(path);
        EnsureNotLink(new FileInfo(manifestPath), "manifest.symlink", "The fixture manifest cannot be a symbolic link.");
        var manifestDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new EventLabInputException("manifest.path-invalid", "The fixture manifest path is invalid.");
        EnsureNotLink(new DirectoryInfo(manifestDirectory), "manifest.symlink", "The fixture directory cannot be a symbolic link.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<FixtureCase>(document.Cases.Count);
        DateTimeOffset? previousReceivedAt = null;
        foreach (var item in document.Cases)
        {
            if (item is null)
                throw new EventLabInputException("manifest.invalid", "Fixture cases cannot be null.");

            ValidateToken(item.Id, "manifest.case-id", "case id");
            if (!ids.Add(item.Id!))
                throw new EventLabInputException("manifest.case-id", "Fixture case ids must be unique.");
            if (item.Adapter is not ("frigate" or "onvif"))
                throw new EventLabInputException("manifest.adapter", "A fixture adapter must be frigate or onvif.");

            ValidatePayloadConvention(item.Adapter, item.Payload!);
            string payloadPath;
            try
            {
                payloadPath = ResolvePayloadPath(manifestDirectory, item.Payload!);
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
                throw new EventLabInputException("manifest.payload-path", "A fixture payload path is invalid.");
            }
            long payloadLength;
            try
            {
                payloadLength = new FileInfo(payloadPath).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new EventLabInputException("fixture.unreadable", "A fixture payload could not be read.");
            }
            if (payloadLength == 0)
                throw new EventLabInputException("fixture.empty", "Fixture payloads cannot be empty.");
            if (payloadLength > document.MaxPayloadBytes)
            {
                throw new EventLabInputException(
                    "fixture.too-large",
                    $"A fixture payload exceeds the {document.MaxPayloadBytes}-byte limit.");
            }
            ValidateText(item.Channel, 256, "manifest.channel", "fixture channel");
            ValidateText(item.ContentType, 256, "manifest.content-type", "fixture contentType");
            ValidateText(item.Note, 1024, "manifest.note", "fixture note");
            var source = ParseSource(item.Source);
            var receivedAt = ParseReceivedAt(item.ReceivedAt);
            if (previousReceivedAt.HasValue && receivedAt < previousReceivedAt.Value)
                throw new EventLabInputException("manifest.received-at", "Fixture receivedAt values must be nondecreasing.");
            previousReceivedAt = receivedAt;

            var hasEventTypes = item.ExpectedEventTypes is not null;
            var hasDiagnostic = item.ExpectedDiagnosticCode is not null;
            if (hasEventTypes == hasDiagnostic)
            {
                throw new EventLabInputException(
                    "manifest.expectation",
                    "Each fixture case must declare exactly one expectation.");
            }

            IReadOnlyList<string>? expectedEventTypes = null;
            if (hasEventTypes)
            {
                if (item.ExpectedEventTypes!.Count == 0 ||
                    item.ExpectedEventTypes.Count > StructuredCloudEventJson.MaxBatchEvents ||
                    item.ExpectedEventTypes.Any(type => type is null || !KnownEventTypes.Contains(type)) ||
                    item.ExpectedEventTypes.Distinct(StringComparer.Ordinal).Count() != item.ExpectedEventTypes.Count)
                {
                    throw new EventLabInputException(
                        "manifest.event-types",
                        "expectedEventTypes must contain from 1 through 256 recognized v1 event types.");
                }
                expectedEventTypes = item.ExpectedEventTypes.ToArray();
            }

            if (hasDiagnostic)
                ValidateToken(item.ExpectedDiagnosticCode, "manifest.diagnostic-code", "expected diagnostic code");

            cases.Add(new FixtureCase(
                item.Id!,
                item.Adapter!,
                item.Payload!,
                payloadPath,
                source,
                item.Channel!,
                item.ContentType!,
                receivedAt,
                expectedEventTypes,
                item.ExpectedDiagnosticCode,
                item.Note!));
        }

        try
        {
            EnsureCorpusCoverage(manifestDirectory, manifestPath, cases);
        }
        catch (EventLabInputException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new EventLabInputException("manifest.corpus", "The fixture corpus could not be inspected.");
        }
        return new FixtureManifest(manifestPath, manifestDirectory, document.MaxPayloadBytes, cases);
    }

    private static void ValidateRequiredJsonShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out _) ||
            !root.TryGetProperty("maxPayloadBytes", out _) ||
            !root.TryGetProperty("cases", out var cases) ||
            cases.ValueKind != JsonValueKind.Array)
        {
            throw new EventLabInputException("manifest.invalid", "The fixture manifest is not valid v1 JSON.");
        }

        var required = new[]
        {
            "id", "adapter", "payload", "source", "channel", "contentType", "receivedAt", "note"
        };
        foreach (var item in cases.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || required.Any(name => !item.TryGetProperty(name, out _)))
                throw new EventLabInputException("manifest.invalid", "The fixture manifest is not valid v1 JSON.");
            var hasTypes = item.TryGetProperty("expectedEventTypes", out _);
            var hasDiagnostic = item.TryGetProperty("expectedDiagnosticCode", out _);
            if (hasTypes == hasDiagnostic)
                throw new EventLabInputException("manifest.expectation", "Each fixture case must declare exactly one expectation.");
        }
    }

    private static Uri ParseSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 2048 ||
            value.Any(char.IsControl) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var source) ||
            !string.Equals(source.Scheme, "urn", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(source.UserInfo) ||
            !string.IsNullOrEmpty(source.Query) ||
            !string.IsNullOrEmpty(source.Fragment))
        {
            throw new EventLabInputException(
                "manifest.source",
                "A fixture source must be an opaque absolute URN of at most 2048 characters.");
        }

        return source;
    }

    private static DateTimeOffset ParseReceivedAt(string? value)
    {
        ValidateText(value, 256, "manifest.received-at", "fixture receivedAt");
        if (!value!.EndsWith('Z') ||
            !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var receivedAt) ||
            receivedAt.Offset != TimeSpan.Zero)
        {
            throw new EventLabInputException(
                "manifest.received-at",
                "Fixture receivedAt values must be explicit UTC timestamps.");
        }

        return receivedAt;
    }

    private static void ValidatePayloadConvention(string adapter, string relativePath)
    {
        var expectedExtension = adapter == "frigate" ? ".json" : ".xml";
        if (!relativePath.StartsWith($"{adapter}/", StringComparison.Ordinal) ||
            !Path.GetExtension(relativePath).Equals(expectedExtension, StringComparison.Ordinal))
        {
            throw new EventLabInputException(
                "manifest.payload-kind",
                "Each fixture payload must use its adapter directory and expected file extension.");
        }
    }

    private static string ResolvePayloadPath(string manifestDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Length > 512 ||
            relativePath.Contains('\\', StringComparison.Ordinal) ||
            relativePath.Any(char.IsControl) ||
            Path.IsPathRooted(relativePath))
        {
            throw new EventLabInputException("manifest.payload-path", "Fixture payload paths must be bounded POSIX relative paths.");
        }

        var segments = relativePath.Split('/', StringSplitOptions.None);
        if (segments.Any(segment =>
                segment.Length == 0 ||
                segment is "." or ".." ||
                segment.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))))
        {
            throw new EventLabInputException(
                "manifest.payload-path",
                "Fixture payload paths must use canonical ASCII segments and cannot traverse directories.");
        }

        var root = Path.GetFullPath(manifestDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(new[] { root }.Concat(segments).ToArray()));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, comparison))
            throw new EventLabInputException("manifest.payload-path", "Fixture payload paths cannot leave the manifest directory.");

        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            FileSystemInfo info = index == segments.Length - 1
                ? new FileInfo(current)
                : new DirectoryInfo(current);
            if (!info.Exists)
                throw new EventLabInputException("fixture.unreadable", "A fixture payload could not be read.");
            EnsureNotLink(info, "manifest.payload-symlink", "Fixture payload paths cannot contain symbolic links.");
        }

        return fullPath;
    }

    private static void EnsureCorpusCoverage(
        string manifestDirectory,
        string manifestPath,
        IReadOnlyList<FixtureCase> cases)
    {
        var referenced = cases
            .Select(item => item.Payload)
            .ToHashSet(StringComparer.Ordinal);
        var discovered = EnumeratePayloadFiles(manifestDirectory)
            .Where(path => !PathComparer.Equals(path, manifestPath))
            .Select(path => Path.GetRelativePath(manifestDirectory, path)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .ToHashSet(StringComparer.Ordinal);
        if (!referenced.SetEquals(discovered))
        {
            throw new EventLabInputException(
                "manifest.coverage",
                "Every JSON or XML payload in the fixture directory must be represented by the manifest.");
        }
    }

    private static IEnumerable<string> EnumeratePayloadFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var visited = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++visited > 4096)
                    throw new EventLabInputException("manifest.corpus-size", "The fixture corpus contains too many entries.");

                if (Directory.Exists(entry))
                {
                    var info = new DirectoryInfo(entry);
                    EnsureNotLink(info, "manifest.payload-symlink", "Fixture payload paths cannot contain symbolic links.");
                    pending.Push(entry);
                    continue;
                }

                var file = new FileInfo(entry);
                EnsureNotLink(file, "manifest.payload-symlink", "Fixture payload paths cannot contain symbolic links.");
                if (file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                    file.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file.FullName;
                }
            }
        }
    }

    private static void EnsureNotLink(FileSystemInfo info, string code, string message)
    {
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new EventLabInputException(code, message);
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new EventLabInputException("manifest.duplicate-property", "The fixture manifest contains a duplicate property.");
                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                EnsureNoDuplicateProperties(item);
        }
    }

    private static void ValidateText(string? value, int maximumLength, string code, string description)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.Any(char.IsControl) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new EventLabInputException(code, $"The {description} is invalid.");
        }
    }

    private static void ValidateToken(string? value, string code, string description)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value[0] is '.' or '-' || value[^1] is '.' or '-' ||
            value.Any(character =>
                !(character is >= 'a' and <= 'z') &&
                !char.IsAsciiDigit(character) &&
                character is not ('.' or '-')))
        {
            throw new EventLabInputException(code, $"The {description} must be a lower-case dot/hyphen token.");
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record ManifestDocument(
        int SchemaVersion,
        int MaxPayloadBytes,
        IReadOnlyList<ManifestCaseDocument?>? Cases);

    private sealed record ManifestCaseDocument(
        string? Id,
        string? Adapter,
        string? Payload,
        string? Source,
        string? Channel,
        string? ContentType,
        string? ReceivedAt,
        IReadOnlyList<string>? ExpectedEventTypes,
        string? ExpectedDiagnosticCode,
        string? Note);
}
