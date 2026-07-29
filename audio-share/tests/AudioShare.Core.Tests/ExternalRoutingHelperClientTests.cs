using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class ExternalRoutingHelperClientTests
{
    [Fact]
    public async Task CheckHealthAsync_RejectsChangedHelperBeforeLaunch()
    {
        using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":{\"version\":\"1.1.1\"}}");
        fixture.WriteManifestWithWrongHash();

        var result = await fixture.CreateClient().CheckHealthAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("integrity", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(fixture.WasLaunched);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnavailableWhenHelperTimesOut()
    {
        using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":{\"version\":\"1.1.1\"}}", FixtureBehavior.Timeout);

        var result = await fixture.CreateClient().CheckHealthAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(fixture.WasLaunched);
    }

    [Fact]
    public async Task CheckHealthAsync_CancellationKillsStartedHelper()
    {
        using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":{\"version\":\"1.1.1\"}}", FixtureBehavior.Timeout);
        using var cancellation = new CancellationTokenSource();
        var task = fixture.CreateClient().CheckHealthAsync(cancellation.Token);

        await fixture.WaitForLaunchAsync();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(await fixture.WaitForHelperExitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task SetRouteAsync_CancellationDuringBlockedStdinWrite_KillsStartedHelper()
    {
        using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":null}", FixtureBehavior.BlockStdin);
        using var cancellation = new CancellationTokenSource();
        var task = fixture.CreateClient().SetRouteAsync(41, new string('x', 1024 * 1024), cancellation.Token);

        await fixture.WaitForLaunchAsync();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(await fixture.WaitForHelperExitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnavailableWhenHelperExitsNonzero()
    {
        using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":{\"version\":\"1.1.1\"}}", FixtureBehavior.NonzeroExit);

        var result = await fixture.CreateClient().CheckHealthAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("exit", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnavailableWhenHelperWritesExtraStdout()
    {
        using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":{\"version\":\"1.1.1\"}}", FixtureBehavior.ExtraStdout);

        var result = await fixture.CreateClient().CheckHealthAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("extra stdout", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnavailableWhenHelperReturnsInvalidJson()
    {
        using var fixture = HelperFixture.Create("not json");

        var result = await fixture.CreateClient().CheckHealthAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("JSON", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnavailableWhenHelperResponseOmitsOk()
    {
        using var fixture = HelperFixture.Create("{\"value\":{\"version\":\"1.1.1\"}}");

        var result = await fixture.CreateClient().CheckHealthAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("missing ok", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnavailableWhenHealthVersionDoesNotMatchManifest()
    {
        using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":{\"version\":\"1.0.0\"}}");

        var result = await fixture.CreateClient().CheckHealthAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("version", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListOutputDevicesAsync_MapsHelperDeviceValues()
    {
        using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":[{\"id\":\"device-a\",\"name\":\"Headphones\"}]}");

        var devices = await fixture.CreateClient().ListOutputDevicesAsync(CancellationToken.None);

        Assert.Equal([new ExternalAudioDevice("device-a", "Headphones")], devices);
        Assert.Equal("list-devices", fixture.Requests.Single().GetProperty("command").GetString());
    }

    [Fact]
    public async Task RouteMethods_SendOnlyTheirMappedJsonRequests()
    {
        using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":\"previous-device\"}");
        var client = fixture.CreateClient();

        var route = await client.GetRouteAsync(41, CancellationToken.None);
        await client.SetRouteAsync(41, "device-a", CancellationToken.None);
        await client.ClearRouteAsync(41, CancellationToken.None);

        Assert.Equal("previous-device", route);
        Assert.Collection(
            fixture.Requests,
            request => AssertRequest(request, "get-route", 41, null),
            request => AssertRequest(request, "set-route", 41, "device-a"),
            request => AssertRequest(request, "clear-route", 41, null));
    }

    [Fact]
    public async Task CheckHealthAsync_LaunchesThePackagedPythonDirectlyWithoutAShell()
    {
        using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":{\"version\":\"1.1.1\"}}");

        var result = await fixture.CreateClient().CheckHealthAsync(CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Equal(fixture.PythonPath, fixture.ParentExecutablePath, ignoreCase: true);
        Assert.Equal(["-I", fixture.HelperPath], fixture.CommandLineArguments);
        Assert.Single(fixture.Requests);
    }

    private static void AssertRequest(JsonElement request, string command, int processId, string? deviceId)
    {
        Assert.Equal(command, request.GetProperty("command").GetString());
        Assert.Equal(processId, request.GetProperty("processId").GetInt32());
        if (deviceId is null)
        {
            Assert.False(request.TryGetProperty("deviceId", out _));
        }
        else
        {
            Assert.Equal(deviceId, request.GetProperty("deviceId").GetString());
        }
    }

    private enum FixtureBehavior
    {
        Normal,
        Timeout,
        BlockStdin,
        NonzeroExit,
        ExtraStdout,
    }

    private sealed class HelperFixture : IDisposable
    {
        private const string ManifestFileName = "router-helper-manifest.json";
        private static readonly Lazy<string> FakePythonPath = new(BuildFakePython);
        private readonly string root;
        private readonly string capturePath;
        private readonly string processPath;
        private readonly string launcherProcessPath;

        private HelperFixture(string response, FixtureBehavior behavior)
        {
            root = Path.Combine(Path.GetTempPath(), $"AudioShare.HelperFixture.{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            HelperPath = Path.Combine(root, "audio_share_router_helper.py");
            capturePath = Path.Combine(root, "requests.jsonl");
            processPath = Path.Combine(root, "process.json");
            launcherProcessPath = Path.Combine(root, "launcher-process.json");
            CopyFakePython(root);
            File.WriteAllText(
                HelperPath,
                JsonSerializer.Serialize(new
                {
                    response,
                    behavior = behavior.ToString(),
                    capturePath,
                    processPath,
                    launcherProcessPath,
                }));
            WriteManifest(FileHash(HelperPath), "1.1.1");
        }

        public string HelperPath { get; }

        public string PackageDirectory => root;

        public string PythonPath => Path.Combine(root, "python.exe");

        public bool WasLaunched => File.Exists(processPath);

        public string ParentExecutablePath => ReadProcessStringValue("parentExecutablePath");

        public IReadOnlyList<string> CommandLineArguments => JsonSerializer.Deserialize<string[]>(ReadLauncherProcessValue("arguments"))!;

        public async Task WaitForLaunchAsync()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            while (!WasLaunched && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.True(WasLaunched);
        }

        public async Task<bool> WaitForHelperExitAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (IsHelperRunning() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            return !IsHelperRunning();
        }

        public IReadOnlyList<JsonElement> Requests => !File.Exists(capturePath)
            ? []
            : File.ReadLines(capturePath).Select(line => JsonDocument.Parse(line)).Select(document => document.RootElement.Clone()).ToArray();

        public static HelperFixture Create(string response, FixtureBehavior behavior = FixtureBehavior.Normal)
        {
            return new HelperFixture(response, behavior);
        }

        public ExternalRoutingHelperClient CreateClient()
        {
            return new ExternalRoutingHelperClient(root);
        }

        public void WriteManifestWithWrongHash()
        {
            WriteManifest(new string('0', 64), "1.1.1");
        }

        public void Dispose()
        {
            if (WasLaunched)
            {
                try
                {
                    using var process = Process.GetProcessById(ReadProcessInt32("processId"));
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                    }
                }
                catch (ArgumentException)
                {
                }
            }

            Directory.Delete(root, recursive: true);
        }

        private static string FileHash(string path)
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        private static string BuildFakePython()
        {
            var fixtureRoot = Path.Combine(Path.GetTempPath(), $"AudioShare.FakePython.{Guid.NewGuid():N}");
            var outputDirectory = Path.Combine(fixtureRoot, "publish");
            Directory.CreateDirectory(fixtureRoot);
            File.WriteAllText(
                Path.Combine(fixtureRoot, "FakePython.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(fixtureRoot, "Program.cs"), """
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args[0] == "--helper")
{
    await RunHelperAsync(args[1]);
    return;
}
var configuration = JsonSerializer.Deserialize<FixtureConfiguration>(
    await File.ReadAllTextAsync(args[^1]),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
await File.WriteAllTextAsync(
    configuration.LauncherProcessPath,
    JsonSerializer.Serialize(new { arguments = args }));
using (var helper = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = Environment.ProcessPath!,
        UseShellExecute = false,
    },
})
{
    helper.StartInfo.ArgumentList.Add("--helper");
    helper.StartInfo.ArgumentList.Add(args[^1]);
    helper.Start();
    await helper.WaitForExitAsync();
    Environment.ExitCode = helper.ExitCode;
}

static async Task RunHelperAsync(string configurationPath)
{
var configuration = JsonSerializer.Deserialize<FixtureConfiguration>(
    await File.ReadAllTextAsync(configurationPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
await File.WriteAllTextAsync(
    configuration.ProcessPath,
    JsonSerializer.Serialize(new { processId = Environment.ProcessId, parentExecutablePath = GetParentExecutablePath(), arguments = Environment.GetCommandLineArgs().Skip(1) }));
if (configuration.Behavior == "BlockStdin")
{
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}
string? request;
while ((request = await Console.In.ReadLineAsync()) is not null)
{
    await File.AppendAllTextAsync(configuration.CapturePath, request + Environment.NewLine);
}
if (configuration.Behavior == "Timeout")
{
    await Task.Delay(TimeSpan.FromSeconds(6));
}

Console.WriteLine(configuration.Response);
if (configuration.Behavior == "ExtraStdout")
{
    Console.WriteLine("extra output");
}

if (configuration.Behavior == "NonzeroExit")
{
    Environment.Exit(1);
}
}

static string GetParentExecutablePath()
{
    var status = NtQueryInformationProcess(
        Process.GetCurrentProcess().Handle,
        0,
        out var information,
        Marshal.SizeOf<ProcessBasicInformation>(),
        out _);
    if (status != 0)
    {
        throw new InvalidOperationException($"Could not query the fixture parent process: 0x{status:X8}.");
    }

    using var parent = Process.GetProcessById(checked((int)information.InheritedFromUniqueProcessId));
    return parent.MainModule?.FileName
        ?? throw new InvalidOperationException("Could not read the fixture parent executable path.");
}

[DllImport("ntdll.dll")]
static extern int NtQueryInformationProcess(
    IntPtr processHandle,
    int processInformationClass,
    out ProcessBasicInformation processInformation,
    int processInformationLength,
    out int returnLength);

[StructLayout(LayoutKind.Sequential)]
struct ProcessBasicInformation
{
    public IntPtr Reserved1;
    public IntPtr PebBaseAddress;
    public IntPtr Reserved2_0;
    public IntPtr Reserved2_1;
    public IntPtr UniqueProcessId;
    public IntPtr InheritedFromUniqueProcessId;
}

public sealed record FixtureConfiguration(string Response, string Behavior, string CapturePath, string ProcessPath, string LauncherProcessPath);
""");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                },
            };
            process.StartInfo.ArgumentList.Add("publish");
            process.StartInfo.ArgumentList.Add(Path.Combine(fixtureRoot, "FakePython.csproj"));
            process.StartInfo.ArgumentList.Add("--configuration");
            process.StartInfo.ArgumentList.Add("Release");
            process.StartInfo.ArgumentList.Add("--output");
            process.StartInfo.ArgumentList.Add(outputDirectory);
            process.StartInfo.ArgumentList.Add("--nologo");
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Could not build the fake python fixture: {output}{error}");
            }

            return Path.Combine(outputDirectory, "FakePython.exe");
        }

        private static void CopyFakePython(string destinationDirectory)
        {
            var sourceDirectory = Path.GetDirectoryName(FakePythonPath.Value)!;
            foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory))
            {
                var fileName = Path.GetFileName(sourcePath) == "FakePython.exe"
                    ? "python.exe"
                    : Path.GetFileName(sourcePath);
                File.Copy(sourcePath, Path.Combine(destinationDirectory, fileName));
            }
        }

        private void WriteManifest(string helperHash, string version)
        {
            File.WriteAllText(
                Path.Combine(root, ManifestFileName),
                JsonSerializer.Serialize(new RoutingHelperManifest(helperHash, version)));
        }

        private string ReadProcessValue(string propertyName)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(processPath));
            return document.RootElement.GetProperty(propertyName).GetRawText().Trim('"');
        }

        private string ReadProcessStringValue(string propertyName)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(processPath));
            return document.RootElement.GetProperty(propertyName).GetString()!;
        }

        private string ReadLauncherProcessValue(string propertyName)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(launcherProcessPath));
            return document.RootElement.GetProperty(propertyName).GetRawText();
        }

        private int ReadProcessInt32(string propertyName)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(processPath));
            return document.RootElement.GetProperty(propertyName).GetInt32();
        }

        private bool IsHelperRunning()
        {
            try
            {
                using var process = Process.GetProcessById(ReadProcessInt32("processId"));
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

    }
}
