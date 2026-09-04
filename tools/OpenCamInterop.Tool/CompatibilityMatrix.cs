using System.Text;

namespace OpenCamInterop.EventLab;

internal static class CompatibilityMatrix
{
    internal const string FileName = "COMPATIBILITY.md";
    internal const int MaximumBytes = 512 * 1024;

    internal static string Render(FixtureManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!-- Generated from manifest.json. Do not edit by hand. -->");
        builder.AppendLine($"# OpenCamInterop executable fixture matrix v{FixtureManifestLoader.SchemaVersion}");
        builder.AppendLine();
        builder.AppendLine("This matrix records adapter behavior proven by synthetic, offline fixtures. It does not claim device, NVR, firmware, or vendor compatibility and is not an ONVIF conformance statement.");
        builder.AppendLine();
        builder.AppendLine("| Case | Adapter | Payload | Expected result | Sanitized behavior note |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var fixture in manifest.Cases)
        {
            var outcome = fixture.ExpectedEventTypes is { } eventTypes
                ? string.Join("<br>", eventTypes.Select(item => $"event `{EscapeCell(item)}`"))
                : $"diagnostic `{EscapeCell(fixture.ExpectedDiagnosticCode!)}`";
            builder.Append("| `").Append(EscapeCell(fixture.Id)).Append("` | `")
                .Append(EscapeCell(fixture.Adapter)).Append("` | [`")
                .Append(EscapeCell(fixture.Payload)).Append("`](")
                .Append(fixture.Payload).Append(") | ")
                .Append(outcome).Append(" | ")
                .Append(EscapeCell(fixture.Note)).AppendLine(" |");
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    internal static bool IsCurrent(FixtureManifest manifest)
    {
        var path = Path.Combine(manifest.Directory, FileName);
        var actual = BoundedFileReader.Read(path, MaximumBytes, "matrix");
        var expected = Encoding.UTF8.GetBytes(Render(manifest));
        return actual.AsSpan().SequenceEqual(expected);
    }

    private static string EscapeCell(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }
}
