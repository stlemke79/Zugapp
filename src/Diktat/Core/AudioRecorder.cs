using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Diktat;

/// <summary>Nimmt vom Mikrofon in 16 kHz / mono / 16 bit auf – das Format, das Whisper erwartet.</summary>
internal sealed class AudioRecorder : IDisposable
{
    public const int SampleRate = 16000;

    private readonly object _gate = new();
    private readonly List<float> _samples = new();
    private WaveIn? _wave;
    private TaskCompletionSource<bool>? _stopped;

    /// <summary>Lautstärke-Spitzenwert 0..1, ca. 20× pro Sekunde (auf dem Audio-Thread).</summary>
    public event Action<float>? Level;

    public TimeSpan Duration
    {
        get { lock (_gate) return TimeSpan.FromSeconds(_samples.Count / (double)SampleRate); }
    }

    public static IReadOnlyList<string> Devices()
    {
        var list = new List<string>();
        for (int i = 0; i < WaveIn.DeviceCount; i++) list.Add(WaveIn.GetCapabilities(i).ProductName);
        return list;
    }

    public void Start(int device)
    {
        lock (_gate) _samples.Clear();
        _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _wave = new WaveIn
        {
            DeviceNumber = device < WaveIn.DeviceCount ? device : -1,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 50,
        };
        _wave.DataAvailable += OnData;
        _wave.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) Log.Error("Aufnahme abgebrochen", e.Exception);
            _stopped?.TrySetResult(true);
        };
        _wave.StartRecording();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        float peak = 0;
        lock (_gate)
        {
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                float s = BitConverter.ToInt16(e.Buffer, i) / 32768f;
                _samples.Add(s);
                peak = Math.Max(peak, Math.Abs(s));
            }
        }
        Level?.Invoke(peak);
    }

    public async Task<float[]> StopAsync()
    {
        if (_wave is null) return Array.Empty<float>();
        _wave.StopRecording();
        await Task.WhenAny(_stopped!.Task, Task.Delay(2000));
        _wave.Dispose();
        _wave = null;
        lock (_gate) return _samples.ToArray();
    }

    /// <summary>Grobe Prüfung, ob überhaupt gesprochen wurde (verhindert Whisper-„Halluzinationen“ bei Stille).</summary>
    public static bool ContainsSpeech(float[] samples)
    {
        const int window = SampleRate / 50; // 20 ms
        int loudWindows = 0;
        for (int start = 0; start + window <= samples.Length; start += window)
        {
            double sum = 0;
            for (int i = start; i < start + window; i++) sum += samples[i] * samples[i];
            if (Math.Sqrt(sum / window) > 0.015) loudWindows++;
        }
        return loudWindows >= 10; // mind. ~0,2 s hörbares Signal
    }

    public void Dispose()
    {
        _wave?.Dispose();
        _wave = null;
    }
}
