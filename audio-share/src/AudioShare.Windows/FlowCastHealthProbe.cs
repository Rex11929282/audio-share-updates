using System.Diagnostics;
using AudioShare.Core;
using NAudio.CoreAudioApi;

namespace AudioShare.Windows;

public sealed record FlowCastHealthProbeResult(
    HealthSummary Summary,
    bool BananaInstalled,
    bool RoutingAvailable,
    string RoutingMessage,
    VoicemeeterBananaEndpoints? Endpoints,
    IReadOnlyList<ExternalAudioDevice> OutputDevices);

public sealed class FlowCastHealthProbe
{
    private readonly ExternalRoutingHelperClient routingHelper;

    public FlowCastHealthProbe(ExternalRoutingHelperClient routingHelper)
    {
        this.routingHelper = routingHelper;
    }

    public async Task<FlowCastHealthProbeResult> CheckAsync(CancellationToken token)
    {
        var bananaInstalled = VoicemeeterBananaInstallationDetector.IsInstalled();
        var bananaRunning = IsProcessRunning("voicemeeterpro");
        var hasDefaultPlayback = HasDefaultPlayback();
        var helperHealth = await routingHelper.CheckHealthAsync(token);
        if (!helperHealth.IsAvailable)
        {
            return new FlowCastHealthProbeResult(
                HealthSummary.Create(bananaRunning, hasDefaultPlayback, false, false, routingHelperReady: false),
                bananaInstalled,
                false,
                helperHealth.Message,
                null,
                []);
        }

        try
        {
            var devices = await routingHelper.ListOutputDevicesAsync(token);
            var endpointHealth = VoicemeeterBananaDetector.GetEndpointHealth(devices);
            var hasEndpoints = VoicemeeterBananaDetector.TryFindEndpoints(devices, out var endpoints);
            return new FlowCastHealthProbeResult(
                HealthSummary.Create(
                    bananaRunning,
                    hasDefaultPlayback,
                    endpointHealth.HasInput,
                    endpointHealth.HasAux),
                bananaInstalled,
                hasEndpoints,
                hasEndpoints ? "音頻路由已就緒。" : "未檢測到 Voicemeeter Banana 的必要音頻設備。",
                endpoints,
                devices);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            return new FlowCastHealthProbeResult(
                HealthSummary.Create(bananaRunning, hasDefaultPlayback, false, false, routingHelperReady: false),
                bananaInstalled,
                false,
                exception.Message,
                null,
                []);
        }
    }

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static bool HasDefaultPlayback()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device is not null;
        }
        catch
        {
            return false;
        }
    }
}
