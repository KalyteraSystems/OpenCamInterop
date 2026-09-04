namespace OpenCamInterop;

public sealed record CameraObjectEventData(
    string Adapter,
    string CameraId,
    string ObjectId,
    string Label,
    double? Confidence,
    double? TopConfidence,
    IReadOnlyList<string> CurrentZones,
    IReadOnlyList<string> EnteredZones,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

public sealed record OnvifSimpleItem(string Name, string Value);

public sealed record CameraSignalEventData(
    string Adapter,
    string CameraId,
    string? RuleId,
    string Signal,
    bool Active,
    string PropertyOperation);

public sealed record OnvifNotificationEventData(
    string Adapter,
    string TopicNamespace,
    string Topic,
    string? PropertyOperation,
    IReadOnlyList<OnvifSimpleItem> SourceItems,
    IReadOnlyList<OnvifSimpleItem> KeyItems,
    IReadOnlyList<OnvifSimpleItem> DataItems);
