namespace AudioShare.Core;

public sealed record SharingBusStatus(
    bool IsMainInputShared,
    bool IsAuxShared,
    float InputLevel,
    float B1Level);
