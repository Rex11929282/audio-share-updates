namespace AudioShare.Core;

public interface IProcessEndpointRouter
{
    Task SetEndpointAsync(int processId, string endpointId, CancellationToken token);

    Task ClearEndpointAsync(int processId, CancellationToken token);
}
