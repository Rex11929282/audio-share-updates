namespace AudioShare.Core;

public sealed record AudioFrame
{
    public AudioFrame(AudioFormat format, long timestampTicks, float[] samples)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Length % format.SamplesPerFrame != 0)
        {
            throw new ArgumentException("Sample count must fill whole frames.", nameof(samples));
        }

        Format = format;
        TimestampTicks = timestampTicks;
        Samples = samples;
    }

    public AudioFormat Format { get; }

    public long TimestampTicks { get; }

    public float[] Samples { get; }

    public int FrameCount => Samples.Length / Format.SamplesPerFrame;
}
