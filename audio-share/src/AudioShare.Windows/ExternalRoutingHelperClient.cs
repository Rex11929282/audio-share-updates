using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace AudioShare.Windows;

public sealed class ExternalRoutingHelperClient : IExternalRoutingHelper
{
    private const string PythonFileName = "python.exe";
    private const string HelperFileName = "audio_share_router_helper.py";
    private const string ManifestFileName = "router-helper-manifest.json";
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string packageDirectory;

    public ExternalRoutingHelperClient(string packageDirectory)
    {
        this.packageDirectory = packageDirectory ?? throw new ArgumentNullException(nameof(packageDirectory));
    }

    public async Task<ExternalRoutingHealth> CheckHealthAsync(CancellationToken token)
    {
        try
        {
            var (manifest, response) = await InvokeAsync(new { command = "health" }, token);
            if (response.ValueKind != JsonValueKind.Object ||
                !response.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.String ||
                !string.Equals(version.GetString(), manifest.RouterVersion, StringComparison.Ordinal))
            {
                return new ExternalRoutingHealth(false, "Routing helper health version does not match the package manifest.");
            }

            return new ExternalRoutingHealth(true, "Routing helper is available.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ExternalRoutingHealth(false, exception.Message);
        }
    }

    public async Task<IReadOnlyList<ExternalAudioDevice>> ListOutputDevicesAsync(CancellationToken token)
    {
        var (_, value) = await InvokeAsync(new { command = "list-devices" }, token);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Helper response has an invalid device list.");
        }

        var devices = new List<ExternalAudioDevice>();
        foreach (var device in value.EnumerateArray())
        {
            if (device.ValueKind != JsonValueKind.Object ||
                !device.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                !device.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("Helper response has an invalid device.");
            }

            devices.Add(new ExternalAudioDevice(id.GetString()!, name.GetString()!));
        }

        return devices;
    }

    public async Task<string?> GetRouteAsync(int processId, CancellationToken token)
    {
        var (_, value) = await InvokeAsync(new { command = "get-route", processId }, token);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new InvalidOperationException("Helper response has an invalid route."),
        };
    }

    public async Task SetRouteAsync(int processId, string deviceId, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        await InvokeAsync(new { command = "set-route", processId, deviceId }, token);
    }

    public async Task ClearRouteAsync(int processId, CancellationToken token)
    {
        await InvokeAsync(new { command = "clear-route", processId }, token);
    }

    private async Task<(RoutingHelperManifest Manifest, JsonElement Value)> InvokeAsync(object request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var manifest = await ValidatePackageAsync(token);
        var pythonPath = Path.Combine(packageDirectory, PythonFileName);
        var helperPath = Path.Combine(packageDirectory, HelperFileName);
        if (!File.Exists(pythonPath))
        {
            throw new InvalidOperationException("Packaged python.exe is missing.");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-I");
        process.StartInfo.ArgumentList.Add(helperPath);

        if (!process.Start())
        {
            throw new InvalidOperationException("Packaged routing helper could not be started.");
        }

        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(HelperTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw new InvalidOperationException("Routing helper timed out after 5 seconds.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Routing helper exited with code {process.ExitCode}: {stderr.Trim()}");
        }

        return (manifest, ParseResponse(stdout));
    }

    private async Task<RoutingHelperManifest> ValidatePackageAsync(CancellationToken token)
    {
        var helperPath = Path.Combine(packageDirectory, HelperFileName);
        var manifestPath = Path.Combine(packageDirectory, ManifestFileName);
        if (!File.Exists(helperPath) || !File.Exists(manifestPath))
        {
            throw new InvalidOperationException("Routing helper package is incomplete.");
        }

        RoutingHelperManifest? manifest;
        try
        {
            await using var manifestStream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<RoutingHelperManifest>(manifestStream, JsonOptions, token);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Routing helper manifest is invalid.", exception);
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.HelperSha256) || string.IsNullOrWhiteSpace(manifest.RouterVersion))
        {
            throw new InvalidOperationException("Routing helper manifest is incomplete.");
        }

        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(manifest.HelperSha256);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Routing helper integrity manifest is invalid.", exception);
        }

        var actualHash = SHA256.HashData(await File.ReadAllBytesAsync(helperPath, token));
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
        {
            throw new InvalidOperationException("Routing helper integrity verification failed.");
        }

        return manifest;
    }

    private static JsonElement ParseResponse(string stdout)
    {
        var normalized = stdout.Replace("\r\n", "\n", StringComparison.Ordinal);
        var newlineIndex = normalized.IndexOf('\n');
        if (newlineIndex >= 0 && newlineIndex != normalized.Length - 1)
        {
            throw new InvalidOperationException("Routing helper wrote extra stdout.");
        }

        var line = newlineIndex < 0 ? normalized : normalized[..newlineIndex];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Routing helper returned invalid JSON.", exception);
        }

        using (document)
        {
            var response = document.RootElement;
            if (response.ValueKind != JsonValueKind.Object ||
                !response.TryGetProperty("ok", out var ok) ||
                (ok.ValueKind != JsonValueKind.True && ok.ValueKind != JsonValueKind.False))
            {
                throw new InvalidOperationException("Routing helper response is missing ok.");
            }

            if (!ok.GetBoolean())
            {
                var message = response.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : "Routing helper request failed.";
                throw new InvalidOperationException(message);
            }

            if (!response.TryGetProperty("value", out var value))
            {
                throw new InvalidOperationException("Routing helper response is missing value.");
            }

            return value.Clone();
        }
    }
}
