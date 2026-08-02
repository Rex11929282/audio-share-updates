using System.Media;
using System.IO;

namespace AudioShare.App;

internal static class StartupChime
{
    private static readonly MemoryStream Wave = new(CreateWave());
    private static readonly SoundPlayer Player = new(Wave);

    public static void Play()
    {
        try
        {
            Wave.Position = 0;
            Player.Play();
        }
        catch
        {
            // The visual intro must still work when Windows sound is unavailable.
        }
    }

    private static byte[] CreateWave()
    {
        const int sampleRate = 44100;
        const int sampleCount = (int)(sampleRate * 0.46);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + sampleCount * 2);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(sampleCount * 2);

        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (double)sampleRate;
            var frequency = time < 0.16 ? 659.25 : 987.77;
            var fade = Math.Min(1, time / 0.02) * Math.Min(1, (sampleCount / (double)sampleRate - time) / 0.12);
            var fundamental = Math.Sin(2 * Math.PI * frequency * time);
            var harmonic = Math.Sin(2 * Math.PI * frequency * 2 * time) * 0.08;
            var sample = (fundamental + harmonic) * 0.17 * fade;
            writer.Write((short)(sample * short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }
}
