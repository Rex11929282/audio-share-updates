using AudioShare.Core;
using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class ApplicationRouteExecutorTests
{
    [Fact]
    public async Task ApplyAsync_WhenSecondWriteFails_RestoresTheFirstSnapshot()
    {
        var helper = new RecordingHelper(
            new Dictionary<int, string?> { [1] = "old-input", [2] = null },
            failOnSetProcessId: 2);
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([new(1, "chrome.exe", "input"), new(2, "cloudmusic.exe", "aux")]);

        var result = await executor.ApplyAsync(plan, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("set:1:old-input", helper.Calls);
    }

    [Fact]
    public async Task ApplyAsync_ReadsEverySnapshotBeforeTheFirstWrite()
    {
        var helper = new RecordingHelper(new Dictionary<int, string?> { [1] = "old-input", [2] = "old-aux" });
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([new(1, "chrome.exe", "input"), new(2, "cloudmusic.exe", "aux")]);

        var result = await executor.ApplyAsync(plan, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["get:1", "get:2", "set:1:input", "set:2:aux"], helper.Calls);
    }

    [Fact]
    public async Task RestoreAsync_WhenPriorRouteWasNull_ClearsTheRoute()
    {
        var helper = new RecordingHelper(new Dictionary<int, string?> { [1] = null });
        var executor = new ApplicationRouteExecutor(helper);
        var plan = new ApplicationRoutePlan([new(1, "chrome.exe", "input")]);

        await executor.ApplyAsync(plan, CancellationToken.None);
        var result = await executor.RestoreAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("clear:1", helper.Calls);
    }

    [Fact]
    public async Task ApplyAsync_WhenPlanIsEmpty_DoesNotCallTheHelper()
    {
        var helper = new RecordingHelper(new Dictionary<int, string?>());
        var executor = new ApplicationRouteExecutor(helper);

        var result = await executor.ApplyAsync(new ApplicationRoutePlan([]), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("empty", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(helper.Calls);
    }

    [Fact]
    public async Task RestoreAsync_BeforeSuccessfulApply_DoesNotCallTheHelper()
    {
        var helper = new RecordingHelper(new Dictionary<int, string?>());
        var executor = new ApplicationRouteExecutor(helper);

        var result = await executor.RestoreAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(helper.Calls);
    }

    private sealed class RecordingHelper : IExternalRoutingHelper
    {
        private readonly IReadOnlyDictionary<int, string?> routes;
        private readonly int? failOnSetProcessId;

        public RecordingHelper(IReadOnlyDictionary<int, string?> routes, int? failOnSetProcessId = null)
        {
            this.routes = routes;
            this.failOnSetProcessId = failOnSetProcessId;
        }

        public List<string> Calls { get; } = [];

        public Task<string?> GetRouteAsync(int processId, CancellationToken token)
        {
            Calls.Add($"get:{processId}");
            return Task.FromResult(routes.GetValueOrDefault(processId));
        }

        public Task SetRouteAsync(int processId, string deviceId, CancellationToken token)
        {
            Calls.Add($"set:{processId}:{deviceId}");
            if (processId == failOnSetProcessId && deviceId is "aux")
            {
                throw new InvalidOperationException("Route write failed.");
            }

            return Task.CompletedTask;
        }

        public Task ClearRouteAsync(int processId, CancellationToken token)
        {
            Calls.Add($"clear:{processId}");
            return Task.CompletedTask;
        }
    }
}
