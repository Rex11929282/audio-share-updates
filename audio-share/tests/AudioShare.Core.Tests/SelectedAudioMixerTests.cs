using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class SelectedAudioMixerTests
{
    [Fact]
    public void Mix_UsesOnlySelectedProcessFramesAndEnabledMicrophoneFrame()
    {
        var format = new AudioFormat(48_000, 2);
        var mixer = new SelectedAudioMixer();

        var mixed = mixer.Mix(
            format,
            100,
            [new AudioFrame(format, 100, [0.2f, 0.2f])],
            new AudioFrame(format, 100, [0.3f, 0.3f]));

        Assert.Equal([0.5f, 0.5f], mixed.Samples);
    }

    [Fact]
    public void Mix_WithNoSources_ReturnsSilentFrameWithRequestedFormat()
    {
        var format = new AudioFormat(48_000, 2);
        var mixer = new SelectedAudioMixer();

        var mixed = mixer.Mix(format, 100, [], null);

        Assert.Empty(mixed.Samples);
        Assert.Equal(format, mixed.Format);
        Assert.Equal(100, mixed.TimestampTicks);
    }

    [Fact]
    public void Mix_WithFrameUsingAnotherFormat_ThrowsArgumentException()
    {
        var format = new AudioFormat(48_000, 2);
        var otherFormat = new AudioFormat(44_100, 2);
        var mixer = new SelectedAudioMixer();

        Assert.Throws<ArgumentException>(() => mixer.Mix(
            format,
            100,
            [new AudioFrame(otherFormat, 100, [0.2f, 0.2f])],
            null));
    }
}
