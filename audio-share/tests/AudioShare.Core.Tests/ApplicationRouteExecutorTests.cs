using AudioShare.Core;
using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class ApplicationRouteExecutorTests
{
    [Fact]
    public async Task ApplyAsync_WhenSecondWriteFails_RestoresTheAmbiguousWriteAndEarlierWrite()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new("old-console", "old-multimedia"),
                [2] = new(null, "old-aux"),
            },
            failAfterSetProcessId: 2);
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([
            new(1, 101, "chrome.exe", "input"),
            new(2, 102, "cloudmusic.exe", "aux")]);

        var result = await executor.ApplyAsync(plan, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.HasPendingTransaction);
        Assert.Equal(
            [
                "get:1:101:chrome.exe",
                "get:2:102:cloudmusic.exe",
                "set:1:101:chrome.exe:input",
                "set:2:102:cloudmusic.exe:aux",
                "restore:2:102:cloudmusic.exe:<default>:old-aux",
                "restore:1:101:chrome.exe:old-console:old-multimedia",
            ],
            helper.Calls);
    }

    [Fact]
    public async Task ApplyAsync_WhenCallerCancelsDuringWrite_RestoresTheAmbiguousWrite()
    {
        using var cancellation = new CancellationTokenSource();
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new("old-console", "old-multimedia"),
            },
            cancelDuringSetProcessId: 1,
            cancellation: cancellation);
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([new(1, 101, "chrome.exe", "input")]);

        var result = await executor.ApplyAsync(plan, cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.False(result.HasPendingTransaction);
        Assert.Contains("restore:1:101:chrome.exe:old-console:old-multimedia", helper.Calls);
    }

    [Fact]
    public async Task ApplyAsync_WhenAmbiguousWriteRecoveryFails_RetainsItForRetry()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new("old-console", "old-multimedia"),
            },
            failAfterSetProcessId: 1,
            restoreFailures: new Dictionary<int, int> { [1] = 1 });
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([new(1, 101, "chrome.exe", "input")]);

        var applyResult = await executor.ApplyAsync(plan, CancellationToken.None);
        var restoreResult = await executor.RestoreAsync(CancellationToken.None);

        Assert.False(applyResult.Succeeded);
        Assert.True(applyResult.HasPendingTransaction);
        Assert.Contains("Recovery failed", applyResult.Message);
        Assert.True(restoreResult.Succeeded);
        Assert.False(restoreResult.HasPendingTransaction);
        Assert.Equal(2, helper.Calls.Count(call => call.StartsWith("restore:1:101:chrome.exe:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ApplyAsync_ReadsEveryDualRoleSnapshotBeforeTheFirstWrite()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new("old-console", "old-multimedia"),
                [2] = new(null, "old-aux"),
            });
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([
            new(1, 101, "chrome.exe", "input"),
            new(2, 102, "cloudmusic.exe", "aux")]);

        var result = await executor.ApplyAsync(plan, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.HasPendingTransaction);
        Assert.Equal(
            [
                "get:1:101:chrome.exe",
                "get:2:102:cloudmusic.exe",
                "set:1:101:chrome.exe:input",
                "set:2:102:cloudmusic.exe:aux",
            ],
            helper.Calls);
        Assert.Equal(101, result.Snapshots[0].ProcessStartUtcTicks);
        Assert.Equal("chrome.exe", result.Snapshots[0].ProcessName);
        Assert.Equal(new ApplicationRouteState("old-console", "old-multimedia"), result.Snapshots[0].PreviousRoute);
        Assert.Equal(new ApplicationRouteState(null, "old-aux"), result.Snapshots[1].PreviousRoute);
    }

    [Fact]
    public async Task RestoreAsync_RestoresConsoleAndMultimediaRolesExactly()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new(null, "old-multimedia"),
            });
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([new(1, 101, "chrome.exe", "input")]);

        await executor.ApplyAsync(plan, CancellationToken.None);
        var result = await executor.RestoreAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.HasPendingTransaction);
        Assert.Contains("restore:1:101:chrome.exe:<default>:old-multimedia", helper.Calls);
    }

    [Fact]
    public async Task RestoreAsync_WhenOneSnapshotFails_ContinuesAndRetainsOnlyThatSnapshot()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new("old-1-console", "old-1-multimedia"),
                [2] = new("old-2-console", "old-2-multimedia"),
                [3] = new("old-3-console", "old-3-multimedia"),
            },
            restoreFailures: new Dictionary<int, int> { [3] = 1 });
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([
            new(1, 101, "chrome.exe", "input"),
            new(2, 102, "cloudmusic.exe", "aux"),
            new(3, 103, "game.exe", "input")]);
        await executor.ApplyAsync(plan, CancellationToken.None);

        var firstRestore = await executor.RestoreAsync(CancellationToken.None);
        var secondRestore = await executor.RestoreAsync(CancellationToken.None);

        Assert.False(firstRestore.Succeeded);
        Assert.True(firstRestore.HasPendingTransaction);
        Assert.Single(firstRestore.Snapshots);
        Assert.Equal(3, firstRestore.Snapshots[0].ProcessId);
        Assert.Contains("restore:2:102:cloudmusic.exe:old-2-console:old-2-multimedia", helper.Calls);
        Assert.Contains("restore:1:101:chrome.exe:old-1-console:old-1-multimedia", helper.Calls);
        Assert.True(secondRestore.Succeeded);
        Assert.False(secondRestore.HasPendingTransaction);
        Assert.Equal(2, helper.Calls.Count(call => call.StartsWith("restore:3:103:game.exe:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RestoreAsync_WhenTrackedSessionEnded_DiscardsTheStaleSnapshot()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new("old-console", "old-multimedia"),
            },
            missingOutputSessions: new HashSet<int>([1]));
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([new(1, 101, "chrome.exe", "input")]);

        await executor.ApplyAsync(plan, CancellationToken.None);
        var result = await executor.RestoreAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.HasPendingTransaction);
    }

    [Fact]
    public async Task ApplyAsync_WhenAnAppHasNoCurrentAudioSession_WritesItsFuturePlaybackRoute()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new(null, null),
            },
            missingRouteReadSessions: new HashSet<int>([1]));
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([new(1, 101, "cloudmusic.exe", "input")]);

        var result = await executor.ApplyAsync(plan, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("set:1:101:cloudmusic.exe:input", helper.Calls);
    }

    [Fact]
    public async Task RestoreAsync_WhenPreviousPlaybackDeviceIsMissing_FallsBackToWindowsDefault()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new("missing-device", "missing-device"),
            },
            missingPlaybackDevices: new HashSet<int>([1]));
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([new(1, 101, "chrome.exe", "input")]);

        await executor.ApplyAsync(plan, CancellationToken.None);
        var result = await executor.RestoreAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.HasPendingTransaction);
        Assert.Contains("restore:1:101:chrome.exe:<default>:<default>", helper.Calls);
    }

    [Fact]
    public async Task ApplyAsync_WhenPlanIsEmpty_DoesNotCallTheHelper()
    {
        var helper = new RecordingHelper(new Dictionary<int, ApplicationRouteState>());
        var executor = new ApplicationRouteExecutor(helper);

        var result = await executor.ApplyAsync(new ApplicationRoutePlan([]), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.HasPendingTransaction);
        Assert.Contains("empty", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(helper.Calls);
    }

    [Fact]
    public async Task ApplyAsync_WhenPreviousTransactionIsStillOwned_DoesNotOverwriteItsRestorePoint()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new("old-console", "old-multimedia"),
            });
        var executor = new ApplicationRouteExecutor(helper);
        var firstPlan = new ApplicationRoutePlan([new(1, 101, "chrome.exe", "input")]);
        var secondPlan = new ApplicationRoutePlan([new(1, 101, "chrome.exe", "aux")]);

        await executor.ApplyAsync(firstPlan, CancellationToken.None);
        var result = await executor.ApplyAsync(secondPlan, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.HasPendingTransaction);
        Assert.Contains("restore", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [
                "get:1:101:chrome.exe",
                "set:1:101:chrome.exe:input",
            ],
            helper.Calls);
    }

    [Fact]
    public async Task RestoreAsync_BeforeSuccessfulApply_DoesNotCallTheHelper()
    {
        var helper = new RecordingHelper(new Dictionary<int, ApplicationRouteState>());
        var executor = new ApplicationRouteExecutor(helper);

        var result = await executor.RestoreAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.HasPendingTransaction);
        Assert.Empty(helper.Calls);
    }

    [Fact]
    public async Task CompletePersistentRouting_DiscardsTheRestoreTransactionWithoutChangingTheCurrentRoute()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, ApplicationRouteState>
            {
                [1] = new("old-console", "old-multimedia"),
            });
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([new(1, 101, "chrome.exe", "aux")]);

        await executor.ApplyAsync(plan, CancellationToken.None);
        executor.CompletePersistentRouting();
        var restore = await executor.RestoreAsync(CancellationToken.None);

        Assert.False(restore.Succeeded);
        Assert.False(restore.HasPendingTransaction);
        Assert.Equal(
            [
                "get:1:101:chrome.exe",
                "set:1:101:chrome.exe:aux",
            ],
            helper.Calls);
    }

    private sealed class RecordingHelper : IExternalRoutingHelper
    {
        private readonly IReadOnlyDictionary<int, ApplicationRouteState> routes;
        private readonly int? failAfterSetProcessId;
        private readonly int? cancelDuringSetProcessId;
        private readonly CancellationTokenSource? cancellation;
        private readonly Dictionary<int, int> restoreFailures;
        private readonly IReadOnlySet<int> missingRouteReadSessions;
        private readonly IReadOnlySet<int> missingOutputSessions;
        private readonly IReadOnlySet<int> missingPlaybackDevices;

        public RecordingHelper(
            IReadOnlyDictionary<int, ApplicationRouteState> routes,
            int? failAfterSetProcessId = null,
            int? cancelDuringSetProcessId = null,
            CancellationTokenSource? cancellation = null,
            Dictionary<int, int>? restoreFailures = null,
            IReadOnlySet<int>? missingRouteReadSessions = null,
            IReadOnlySet<int>? missingOutputSessions = null,
            IReadOnlySet<int>? missingPlaybackDevices = null)
        {
            this.routes = routes;
            this.failAfterSetProcessId = failAfterSetProcessId;
            this.cancelDuringSetProcessId = cancelDuringSetProcessId;
            this.cancellation = cancellation;
            this.restoreFailures = restoreFailures ?? [];
            this.missingRouteReadSessions = missingRouteReadSessions ?? new HashSet<int>();
            this.missingOutputSessions = missingOutputSessions ?? new HashSet<int>();
            this.missingPlaybackDevices = missingPlaybackDevices ?? new HashSet<int>();
        }

        public List<string> Calls { get; } = [];

        public Task<ApplicationRouteState> GetRouteAsync(
            int processId,
            long processStartUtcTicks,
            string processName,
            CancellationToken token)
        {
            Calls.Add($"get:{processId}:{processStartUtcTicks}:{processName}");
            if (missingRouteReadSessions.Contains(processId))
            {
                throw new InvalidOperationException("Active output session not found.");
            }

            return Task.FromResult(routes[processId]);
        }

        public Task SetRouteAsync(
            int processId,
            long processStartUtcTicks,
            string processName,
            string deviceId,
            CancellationToken token)
        {
            Calls.Add($"set:{processId}:{processStartUtcTicks}:{processName}:{deviceId}");
            token.ThrowIfCancellationRequested();
            if (processId == cancelDuringSetProcessId)
            {
                cancellation!.Cancel();
                token.ThrowIfCancellationRequested();
            }

            if (processId == failAfterSetProcessId)
            {
                throw new InvalidOperationException("Route write outcome is unknown.");
            }

            return Task.CompletedTask;
        }

        public Task RestoreRouteAsync(
            int processId,
            long processStartUtcTicks,
            string processName,
            ApplicationRouteState route,
            CancellationToken token)
        {
            Calls.Add(
                $"restore:{processId}:{processStartUtcTicks}:{processName}:" +
                $"{route.ConsoleDeviceId ?? "<default>"}:{route.MultimediaDeviceId ?? "<default>"}");
            if (restoreFailures.GetValueOrDefault(processId) > 0)
            {
                restoreFailures[processId]--;
                throw new InvalidOperationException("Route restore failed.");
            }

            if (missingOutputSessions.Contains(processId))
            {
                throw new InvalidOperationException("Active output session not found.");
            }

            if (missingPlaybackDevices.Contains(processId) &&
                route.ConsoleDeviceId is not null)
            {
                throw new InvalidOperationException("Playback device not found.");
            }

            return Task.CompletedTask;
        }
    }
}
