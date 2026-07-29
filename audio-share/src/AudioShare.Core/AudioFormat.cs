namespace AudioShare.Core;

public sealed record AudioFormat
{
    public AudioFormat(int sampleRate, int channels)
    {
        if (sampleRate < 1)
        {
            throw new ArgumentException("Sample rate must be at least one.", nameof(sampleRate));
        }

        if (channels < 1)
        {
            throw new ArgumentException("Channel count must be at least one.", nameof(channels));
        }

        SampleRate = sampleRate;
        Channels = channels;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public int SamplesPerFrame => Channels;
}
