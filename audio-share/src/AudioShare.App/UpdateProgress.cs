namespace AudioShare.App;

public enum UpdateStage
{
    Downloading,
    Verifying,
    Extracting,
    ReadyToRestart,
}

public sealed record UpdateProgress(UpdateStage Stage, string Message, int? Percentage);
