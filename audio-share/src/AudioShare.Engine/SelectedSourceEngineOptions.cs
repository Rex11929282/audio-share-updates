using AudioShare.Core;

namespace AudioShare.Engine;

public sealed record SelectedSourceEngineOptions(AudioFormat Format, bool IncludeMicrophone);
