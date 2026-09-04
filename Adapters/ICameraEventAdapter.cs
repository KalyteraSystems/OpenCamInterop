namespace OpenCamInterop.Adapters;

public interface ICameraEventAdapter
{
    string Id { get; }

    AdapterResult Adapt(AdapterMessage message);
}
