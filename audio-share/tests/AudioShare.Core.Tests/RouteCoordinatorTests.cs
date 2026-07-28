using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class RouteCoordinatorTests
{
    private const string VoicemeeterEndpointId = "voicemeeter-input-endpoint";

    [Fact]
    public async Task ShareAsync_RejectsDiscordWithoutChangingItsEndpoint()
    {
        var router = new RecordingRouter();
        var coordinator = new RouteCoordinator(router, VoicemeeterEndpointId);
        var discord = new AudioSession(100, "Discord.EXE", "Discord", true);

        var result = await coordinator.ShareAsync(discord, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Discord cannot be shared.", result.Message);
        Assert.Empty(router.SetCalls);
        Assert.False(coordinator.IsToolCreatedRoute(discord.ProcessId));
    }

    [Fact]
    public async Task ShareAsync_RejectsDiscordProcessNameWithoutExtension()
    {
        var router = new RecordingRouter();
        var coordinator = new RouteCoordinator(router, VoicemeeterEndpointId);
        var discord = new AudioSession(101, "dIsCoRd", "Discord", true);

        var result = await coordinator.ShareAsync(discord, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Discord cannot be shared.", result.Message);
        Assert.Empty(router.SetCalls);
        Assert.False(coordinator.IsToolCreatedRoute(discord.ProcessId));
    }

    [Fact]
    public async Task ShareAsync_RoutesSelectedProcessToVoicemeeterInput()
    {
        var router = new RecordingRouter();
        var coordinator = new RouteCoordinator(router, VoicemeeterEndpointId);
        var session = new AudioSession(200, "cloudmusic.exe", "NetEase Cloud Music", true);

        var result = await coordinator.ShareAsync(session, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.Message);
        Assert.Equal([(session.ProcessId, VoicemeeterEndpointId)], router.SetCalls);
        Assert.True(coordinator.IsToolCreatedRoute(session.ProcessId));
    }

    [Fact]
    public async Task UnshareAsync_ClearsOnlyTheSelectedToolCreatedOverride()
    {
        var router = new RecordingRouter();
        var coordinator = new RouteCoordinator(router, VoicemeeterEndpointId);
        var selected = new AudioSession(300, "chrome.exe", "Chrome", true);

        await coordinator.ShareAsync(selected, CancellationToken.None);
        var result = await coordinator.UnshareAsync(selected.ProcessId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal([selected.ProcessId], router.ClearCalls);
        Assert.False(coordinator.IsToolCreatedRoute(selected.ProcessId));
    }

    [Fact]
    public async Task UnshareAsync_DoesNotClearRouteNotCreatedByTheTool()
    {
        var router = new RecordingRouter();
        var coordinator = new RouteCoordinator(router, VoicemeeterEndpointId);

        var result = await coordinator.UnshareAsync(400, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(router.ClearCalls);
    }

    [Fact]
    public async Task UnshareAsync_WaitsForShareAndClearsTheCommittedRoute()
    {
        var router = new BlockingSetRouter();
        var coordinator = new RouteCoordinator(router, VoicemeeterEndpointId);
        var session = new AudioSession(500, "chrome.exe", "Chrome", true);

        var shareTask = coordinator.ShareAsync(session, CancellationToken.None);
        await router.SetStarted.Task;
        var unshareTask = coordinator.UnshareAsync(session.ProcessId, CancellationToken.None);

        Assert.False(unshareTask.IsCompleted);
        router.CompleteSet();
        await Task.WhenAll(shareTask, unshareTask);

        Assert.Equal([session.ProcessId], router.ClearCalls);
        Assert.False(coordinator.IsToolCreatedRoute(session.ProcessId));
    }

    [Fact]
    public async Task ShareAsync_WhenRouterAppliesThenCancels_KeepsOwnershipForCleanup()
    {
        using var source = new CancellationTokenSource();
        var router = new AppliesThenCancelsRouter(source);
        var coordinator = new RouteCoordinator(router, VoicemeeterEndpointId);
        var session = new AudioSession(600, "cloudmusic.exe", "NetEase Cloud Music", true);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => coordinator.ShareAsync(session, source.Token));

        Assert.True(router.Applied);
        Assert.True(coordinator.IsToolCreatedRoute(session.ProcessId));

        var result = await coordinator.UnshareAsync(session.ProcessId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal([session.ProcessId], router.ClearCalls);
        Assert.False(coordinator.IsToolCreatedRoute(session.ProcessId));
    }

    [Fact]
    public async Task ShareAsync_WaitsForClearAndTracksTheNewRoute()
    {
        var router = new BlockingClearRouter();
        var coordinator = new RouteCoordinator(router, VoicemeeterEndpointId);
        var session = new AudioSession(700, "chrome.exe", "Chrome", true);

        await coordinator.ShareAsync(session, CancellationToken.None);
        var unshareTask = coordinator.UnshareAsync(session.ProcessId, CancellationToken.None);
        await router.ClearStarted.Task;
        var shareTask = coordinator.ShareAsync(session, CancellationToken.None);

        Assert.False(shareTask.IsCompleted);
        router.CompleteClear();
        await Task.WhenAll(unshareTask, shareTask);

        Assert.Equal(2, router.SetCalls);
        Assert.Equal(1, router.ClearCalls);
        Assert.True(coordinator.IsToolCreatedRoute(session.ProcessId));
    }

    private sealed class RecordingRouter : IProcessEndpointRouter
    {
        public List<(int ProcessId, string EndpointId)> SetCalls { get; } = [];
        public List<int> ClearCalls { get; } = [];

        public Task SetEndpointAsync(int processId, string endpointId, CancellationToken token)
        {
            SetCalls.Add((processId, endpointId));
            return Task.CompletedTask;
        }

        public Task ClearEndpointAsync(int processId, CancellationToken token)
        {
            ClearCalls.Add(processId);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSetRouter : IProcessEndpointRouter
    {
        public TaskCompletionSource SetStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource setCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<int> ClearCalls { get; } = [];

        public async Task SetEndpointAsync(int processId, string endpointId, CancellationToken token)
        {
            SetStarted.SetResult();
            await setCompleted.Task.WaitAsync(token);
        }

        public Task ClearEndpointAsync(int processId, CancellationToken token)
        {
            ClearCalls.Add(processId);
            return Task.CompletedTask;
        }

        public void CompleteSet() => setCompleted.SetResult();
    }

    private sealed class AppliesThenCancelsRouter(CancellationTokenSource source) : IProcessEndpointRouter
    {
        public bool Applied { get; private set; }
        public List<int> ClearCalls { get; } = [];

        public Task SetEndpointAsync(int processId, string endpointId, CancellationToken token)
        {
            Applied = true;
            source.Cancel();
            throw new OperationCanceledException(token);
        }

        public Task ClearEndpointAsync(int processId, CancellationToken token)
        {
            ClearCalls.Add(processId);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingClearRouter : IProcessEndpointRouter
    {
        public TaskCompletionSource ClearStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource clearCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SetCalls { get; private set; }
        public int ClearCalls { get; private set; }

        public Task SetEndpointAsync(int processId, string endpointId, CancellationToken token)
        {
            SetCalls++;
            return Task.CompletedTask;
        }

        public async Task ClearEndpointAsync(int processId, CancellationToken token)
        {
            ClearCalls++;
            ClearStarted.SetResult();
            await clearCompleted.Task.WaitAsync(token);
        }

        public void CompleteClear() => clearCompleted.SetResult();
    }
}
