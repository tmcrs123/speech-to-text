using System.IO;
using NAudio.Wave;

namespace SpeechToText;

internal sealed class AudioCapture : IDisposable
{
    private const int SampleRate = 16000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    private WaveInEvent? _waveIn;
    private MemoryStream? _pcm;

    public event Action<float>? LevelChanged;

    public void Start(int deviceNumber = -1)
    {
        if (_waveIn != null) throw new InvalidOperationException("already recording");

        _pcm = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
            BufferMilliseconds = 50,
            DeviceNumber = deviceNumber,
        };
        _waveIn.DataAvailable += (_, e) =>
        {
            _pcm!.Write(e.Buffer, 0, e.BytesRecorded);
            LevelChanged?.Invoke(ComputeRms(e.Buffer, e.BytesRecorded));
        };
        _waveIn.StartRecording();
    }

    private static float ComputeRms(byte[] buffer, int count)
    {
        int samples = count / 2; // 16-bit samples
        if (samples == 0) return 0f;
        double sum = 0;
        for (int i = 0; i < count - 1; i += 2)
        {
            short s = (short)(buffer[i] | (buffer[i + 1] << 8));
            double v = s / 32768.0;
            sum += v * v;
        }
        return (float)Math.Sqrt(sum / samples);
    }

    public byte[] StopAndGetWav()
    {
        if (_waveIn == null || _pcm == null) throw new InvalidOperationException("not recording");

        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;

        byte[] pcm = _pcm.ToArray();
        _pcm.Dispose();
        _pcm = null;

        return BuildWav(pcm);
    }

    private static byte[] BuildWav(byte[] pcm)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int byteRate = SampleRate * Channels * BitsPerSample / 8;
        short blockAlign = (short)(Channels * BitsPerSample / 8);

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + pcm.Length);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)Channels);
        bw.Write(SampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)BitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(pcm.Length);
        bw.Write(pcm);
        bw.Flush();

        return ms.ToArray();
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _pcm?.Dispose();
    }
}
