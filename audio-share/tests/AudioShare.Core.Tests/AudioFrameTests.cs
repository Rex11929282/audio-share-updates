using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class AudioFrameTests
{
    [Fact]
    public void AudioFrame_RejectsSamplesThatDoNotFillWholeFrames()
    {
        var format = new AudioFormat(48_000, 2);

        Assert.Throws<ArgumentException>(() => new AudioFrame(format, 0, [0.1f, 0.2f, 0.3f]));
    }
}
