using System.Diagnostics;
using AudioShare.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioShare.Windows;

public sealed class WasapiAudioSessionDiscovery : IAudioSessionDiscovery
{
    private const int PeakConfirmationDelayMilliseconds = 65;

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
                        if (session.State == AudioSessionState.AudioSessionStateExpired || session.IsSystemSoundsSession)
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
                        var hasAudio = HasAudible(session, initialPeakLevel);
                        candidates.Add(new AudioSessionCandidate(
                            processId,
                            processStartUtcTicks,
                            process.ProcessName,
                            session.DisplayName,
                            // Inactive sessions are still routable and should remain visible for route setup.
                            IsActive: true,
                            IsSystemSession: false,
                            HasAudio: hasAudio,
                            OutputDeviceId: device.ID,
                            OutputDeviceName: device.FriendlyName));
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

    private static bool HasAudible(AudioSessionControl session, float initialPeakLevel)
    {
        Thread.Sleep(PeakConfirmationDelayMilliseconds);
        var confirmedPeakLevel = session.AudioMeterInformation.MasterPeakValue;
        Thread.Sleep(PeakConfirmationDelayMilliseconds);
        return AudioActivityPolicy.IsAudible(
            session.State == AudioSessionState.AudioSessionStateActive,
            initialPeakLevel,
            confirmedPeakLevel,
            session.AudioMeterInformation.MasterPeakValue);
    }
}
