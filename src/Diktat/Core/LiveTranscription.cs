using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Diktat;

/// <summary>
/// Erkennt schon während der Aufnahme: Sobald genug Audio da ist, wird an einer Sprechpause
/// geschnitten und dieser Abschnitt im Hintergrund erkannt. Nach dem Stoppen muss nur noch der Rest erkannt werden.
/// </summary>
internal sealed class LiveTranscription
{
    private const int Rate = AudioRecorder.SampleRate;
    private const int Window = Rate / 50;                 // 20 ms
    private static readonly int MinChunk = 8 * Rate;      // frühestens nach 8 s schneiden …
    private static readonly int MaxChunk = 25 * Rate;     // … spätestens nach 25 s (Whisper verarbeitet max. 30 s am Stück)
    private const int PauseWindows = 15;                  // 300 ms Stille = Sprechpause
    private const double SilenceRms = 0.01;

    private readonly Transcriber _transcriber;
    private readonly AppSettings _settings;
    private readonly CancellationToken _ct;
    private readonly List<string> _texts = new();
    private Task _chain = Task.CompletedTask;
    private int _consumed;

    public int ChunkCount { get; private set; }

    public LiveTranscription(Transcriber transcriber, AppSettings settings, CancellationToken ct)
    {
        _transcriber = transcriber;
        _settings = settings;
        _ct = ct;
    }

    /// <summary>Regelmäßig während der Aufnahme aufrufen; schneidet ggf. den nächsten Abschnitt ab.</summary>
    public void Poll(AudioRecorder recorder)
    {
        int total = recorder.SampleCount;
        if (total - _consumed < MinChunk) return;

        var pending = recorder.Snapshot(_consumed, total - _consumed);
        int cut = FindPause(pending);
        if (cut < 0 && pending.Length >= MaxChunk) cut = QuietestPoint(pending, pending.Length - 3 * Rate, pending.Length);
        if (cut <= 0) return;

        Enqueue(pending[..cut]);
        _consumed += cut;
    }

    /// <summary>Nach dem Stoppen: Rest erkennen und den gesamten Text liefern.</summary>
    public async Task<string> FinishAsync(float[] allSamples)
    {
        if (allSamples.Length > _consumed) Enqueue(allSamples[_consumed..]);
        _consumed = allSamples.Length;
        await _chain;
        return string.Join(" ", _texts).Trim();
    }

    private void Enqueue(float[] chunk)
    {
        ChunkCount++;
        var previous = _chain;
        _chain = RunAsync(previous, chunk);
    }

    private async Task RunAsync(Task previous, float[] chunk)
    {
        await previous;
        if (!AudioRecorder.ContainsSpeech(chunk)) return;

        // Das Ende des bisher Erkannten als Kontext mitgeben, damit Sätze über die Schnittstelle hinweg stimmen.
        string context = _texts.Count > 0 ? Tail(_texts[^1], 200) : "";
        // Whisper rechnet auf einem Hintergrund-Thread, damit Tastenkürzel und Anzeige flüssig bleiben.
        var text = await Task.Run(() => _transcriber.TranscribeAsync(chunk, _settings, _ct, context), _ct);
        if (!string.IsNullOrWhiteSpace(text)) _texts.Add(text);
    }

    /// <summary>Sucht die letzte Sprechpause (≥ 300 ms) nach den ersten 8 s; liefert die Mitte der Pause.</summary>
    private static int FindPause(float[] s)
    {
        int windows = s.Length / Window;
        int quietRun = 0;
        int best = -1;
        for (int w = 0; w < windows; w++)
        {
            if (Rms(s, w * Window) < SilenceRms)
            {
                quietRun++;
                int pauseStartWindow = w - quietRun + 1;
                if (quietRun >= PauseWindows && pauseStartWindow * Window >= MinChunk)
                    best = (pauseStartWindow + quietRun / 2) * Window;
            }
            else
            {
                quietRun = 0;
            }
        }
        // Nicht direkt am Ende schneiden – da könnte gerade ein Wort beginnen.
        return best >= 0 && best <= s.Length - 5 * Window ? best : -1;
    }

    private static int QuietestPoint(float[] s, int from, int to)
    {
        from = Math.Max(0, from);
        double min = double.MaxValue;
        int pos = to;
        for (int i = from; i + Window <= to; i += Window)
        {
            double r = Rms(s, i);
            if (r < min) { min = r; pos = i + Window / 2; }
        }
        return pos;
    }

    private static double Rms(float[] s, int start)
    {
        double sum = 0;
        for (int i = start; i < start + Window && i < s.Length; i++) sum += s[i] * s[i];
        return Math.Sqrt(sum / Window);
    }

    private static string Tail(string text, int max) => text.Length <= max ? text : text[^max..];
}
