using System.Diagnostics;
using AudioShare.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioShare.Windows;

public sealed class WasapiAudioSessionDiscovery : IAudioSessionDiscovery
{
    private const int PeakConfirmationDelayMilliseconds = 125;

    public Task<IReadOnlyList<AudioSession>> GetActiveSessionsAsync(CancellationToken token) =>
        Task.Run(() => Discover(token), token);

    private static IReadOnlyList<AudioSession> Discover(CancellationToken token)
    {
        var candidates = new List<AudioSessionCandidate>();

        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        foreach (var device in devices)
        {
            token.ThrowIfCancellationRequested();

            using (device)
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (var index = 0; index < sessions.Count; index++)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        using var session = sessions[index];
                        if (session.State != AudioSessionState.AudioSessionStateActive || session.IsSystemSoundsSession)
                        {
                            continue;
                        }

                        var processId = (int)session.GetProcessID;
                        if (processId <= 0)
                        {
                            continue;
                        }

                        using var process = Process.GetProcessById(processId);
                        var processStartUtcTicks = process.StartTime.ToUniversalTime().Ticks;
                        var initialPeakLevel = session.AudioMeterInformation.MasterPeakValue;
                        var hasAudio = initialPeakLevel >= AudioActivityPolicy.MinimumPeakLevel &&
                            HasConfirmedOutput(session, initialPeakLevel);
                        candidates.Add(new AudioSessionCandidate(
                            processId,
                            processStartUtcTicks,
                            process.ProcessName,
                            session.DisplayName,
                            IsActive: true,
                            IsSystemSession: false,
                            HasAudio: hasAudio));
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        // Core Audio sessions and their owning processes can disappear mid-enumeration.
                    }
                }
            }
        }

        return AudioSessionFilter.GetActiveProcessSessions(candidates);
    }

    private static bool HasConfirmedOutput(AudioSessionControl session, float initialPeakLevel)
    {
        Thread.Sleep(PeakConfirmationDelayMilliseconds);
        return AudioActivityPolicy.HasConfirmedOutput(
            initialPeakLevel,
            session.AudioMeterInformation.MasterPeakValue);
    }
}
