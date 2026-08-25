using AudioShare.Core;
using AudioShare.Windows;
using System.IO;

namespace AudioShare.App;

public sealed class FlowCastRuntimeHost
{
    private readonly IShareRouteRuntime runtime;
    private readonly IShareRecoveryJournal journal;
    private readonly Func<CancellationToken, Task<bool>> startBanana;
    private readonly Func<CancellationToken, Task<bool>> startVoicemod;

    public FlowCastRuntimeHost(
        IShareRouteRuntime runtime,
        IShareRecoveryJournal journal,
        Func<CancellationToken, Task<bool>> startBanana,
        Func<CancellationToken, Task<bool>> startVoicemod)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        this.startBanana = startBanana ?? throw new ArgumentNullException(nameof(startBanana));
        this.startVoicemod = startVoicemod ?? throw new ArgumentNullException(nameof(startVoicemod));
    }

    public string? RecoveryMessage { get; private set; }

    public async Task InitializeAsync(CancellationToken token)
    {
        await TryStartVoicemodAsync(token);
        await TryStartBananaAsync(token);

        ShareRecoveryRecord? recovery;
        try
        {
            recovery = await journal.ReadAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            RecoveryMessage = exception.Message;
            return;
        }

        if (recovery is null)
        {
            return;
        }

        try
        {
            await runtime.SetSharingBusAsync(false, CancellationToken.None);
            var restored = await runtime.RestoreAsync(recovery.Snapshots, CancellationToken.None);
            if (restored.Succeeded)
            {
                await journal.ClearAsync();
                return;
            }

            RecoveryMessage = restored.TechnicalMessage ?? "The previous audio route is still waiting to be restored.";
        }
        catch (Exception exception)
        {
            // Keep the journal so the next launch can safely retry the original route restore.
            RecoveryMessage = exception.Message;
        }
    }

    public static FlowCastRuntimeHost CreateDefault()
    {
        var routingHelper = new ExternalRoutingHelperClient(Path.Combine(AppContext.BaseDirectory, "router-helper"));
        var runtime = new FlowCastRouteRuntime(
            async token =>
            {
                var devices = await routingHelper.ListOutputDevicesAsync(token);
                if (!VoicemeeterBananaDetector.TryFindEndpoints(devices, out var endpoints) || endpoints is null)
                {
                    throw new InvalidOperationException("Voicemeeter Banana Input and AUX devices are not ready.");
                }

                return new FlowCastEndpointSet(endpoints.Input.Id, endpoints.AuxInput.Id);
            },
            new ApplicationRouteExecutor(routingHelper),
            routingHelper,
            new VoicemeeterSharingBusService());

        var bananaWindow = new VoicemeeterBananaWindowController();
        var voicemod = new OptionalVoicemodLauncher();
        return new FlowCastRuntimeHost(
            runtime,
            new FlowCastRecoveryJournal(),
            bananaWindow.EnsureReadyAndMinimizeAsync,
            voicemod.EnsureReadyAsync);
    }

    private async Task<bool> TryStartBananaAsync(CancellationToken token)
    {
        try
        {
            return await startBanana(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Banana is optional during startup; FlowCast can still recover routes later.
            return false;
        }
    }

    private async Task<bool> TryStartVoicemodAsync(CancellationToken token)
    {
        try
        {
            return await startVoicemod(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Voicemod remains optional, so a failed launch cannot block FlowCast.
            return false;
        }
    }
}
