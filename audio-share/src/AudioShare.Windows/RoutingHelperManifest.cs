namespace AudioShare.Windows;

public sealed record RoutingHelperManifest(string HelperSha256, string RouterVersion);

public sealed record ExternalRoutingHealth(bool IsAvailable, string Message);

public sealed record ExternalAudioDevice(string Id, string Name);
