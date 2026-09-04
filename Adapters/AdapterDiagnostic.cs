namespace OpenCamInterop.Adapters;

public enum AdapterDiagnosticSeverity
{
    Warning,
    Error
}

public sealed record AdapterDiagnostic(
    string Code,
    string Message,
    AdapterDiagnosticSeverity Severity,
    string? Path = null)
{
    public static AdapterDiagnostic Warning(string code, string message, string? path = null)
    {
        return new AdapterDiagnostic(code, message, AdapterDiagnosticSeverity.Warning, path);
    }

    public static AdapterDiagnostic Error(string code, string message, string? path = null)
    {
        return new AdapterDiagnostic(code, message, AdapterDiagnosticSeverity.Error, path);
    }
}
