using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class VoicemeeterSharingBusServiceContractTests
{
    [Fact]
    public void ExposesExplicitAuxB1DisableOperation()
    {
        var method = typeof(VoicemeeterSharingBusService).GetMethod(nameof(VoicemeeterSharingBusService.DisableAuxInputSharing));

        Assert.NotNull(method);
        Assert.Empty(method!.GetParameters());
        Assert.Equal(typeof(void), method.ReturnType);
    }
}
