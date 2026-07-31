using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class SharingRecoveryScopeTests
{
    [Fact]
    public void IncludeCurrent_KeepsFormerlySharedSessionAfterItIsExcludedFromFutureSharing()
    {
        var chrome = new AudioSession(10, 100, "chrome.exe", "Chrome", true);
        var preferences = FlowCastPreferences.Empty.Exclude("chrome.exe");
        var scope = new SharingRecoveryScope();
        scope.Track([chrome]);

        var futureSharingSessions = new[] { chrome }
            .Where(session => !preferences.IsExcluded(session.ProcessName))
            .ToArray();
        var recoverySessions = scope.IncludeCurrent(futureSharingSessions);
        var localOnlyPlan = ApplicationRoutePlanner.Create(recoverySessions, [], "input", "aux");

        var command = Assert.Single(localOnlyPlan.Commands);
        Assert.Equal(chrome.ProcessId, command.ProcessId);
        Assert.Equal("aux", command.TargetDeviceId);
    }
}
