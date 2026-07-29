namespace AudioShare.Core;

public sealed class SelectedAudioMixer
{
    public AudioFrame Mix(
        AudioFormat format,
        long timestampTicks,
        IReadOnlyList<AudioFrame> selectedProcessFrames,
        AudioFrame? microphoneFrame)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(selectedProcessFrames);

        var frames = new List<AudioFrame>(selectedProcessFrames.Count + (microphoneFrame is null ? 0 : 1));
        frames.AddRange(selectedProcessFrames);
        if (microphoneFrame is not null)
        {
            frames.Add(microphoneFrame);
        }

        foreach (var frame in frames)
        {
            if (frame.Format != format)
            {
                throw new ArgumentException("Every source frame must use the requested format.", nameof(selectedProcessFrames));
            }
        }

        var samples = frames.Count == 0 ? [] : new float[frames[0].Samples.Length];
        foreach (var frame in frames)
        {
            for (var index = 0; index < samples.Length && index < frame.Samples.Length; index++)
            {
                samples[index] += frame.Samples[index];
            }
        }

        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = Math.Clamp(samples[index], -1f, 1f);
        }

        return new AudioFrame(format, timestampTicks, samples);
    }
}
