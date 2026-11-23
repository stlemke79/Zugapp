using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace Diktat;

internal sealed record WhisperModelInfo(string Id, string FileName, string Label, long ApproxBytes);

/// <summary>Lokale Spracherkennung mit Whisper (whisper.cpp über Whisper.net). Läuft offline.</summary>
internal sealed class Transcriber : IDisposable
{
    public static readonly WhisperModelInfo[] Models =
    {
        new("base", "ggml-base.bin", "Schnell – einfache Qualität (142 MB)", 147_951_465),
        new("small", "ggml-small.bin", "Ausgewogen – empfohlen (466 MB)", 487_601_967),
        new("large-v3-turbo-q5_0", "ggml-large-v3-turbo-q5_0.bin", "Beste Qualität – braucht schnellen PC (547 MB)", 574_041_195),
    };

    private const string DownloadBase = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    // Typische Whisper-Halluzinationen bei Stille/Rauschen (stammen aus den Trainingsdaten).
    private static readonly Regex Hallucinations = new(
        @"(Untertitel(ung)?\s+(im Auftrag des ZDF|der Amara\.org-Community|von Stephanie Geiges)[^.\n]*\.?|Vielen Dank (fürs|für's|für das) Zuschauen[.!]?|\[[^\]]*\]|\((Musik|Applaus|Lachen)[^)]*\))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private WhisperFactory? _factory;
    private string? _loadedPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public static WhisperModelInfo Model(string id) => Models.FirstOrDefault(m => m.Id == id) ?? Models[1];

    public static string ModelPath(string id) => Path.Combine(AppPaths.Models, Model(id).FileName);

    public static bool IsDownloaded(string id) => File.Exists(ModelPath(id));

    public static async Task DownloadAsync(string id, IProgress<double> progress, CancellationToken ct)
    {
        var info = Model(id);
        var target = ModelPath(id);
        var partial = target + ".part";

        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Diktat/0.1");
        using var response = await http.GetAsync(DownloadBase + info.FileName, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? info.ApproxBytes;

        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = File.Create(partial))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress.Report(Math.Min(1.0, done / (double)total));
            }
        }
        File.Move(partial, target, overwrite: true);
    }

    /// <summary>Lädt das Modell vorab in den Speicher, damit das erste Diktat nicht warten muss.</summary>
    public async Task WarmUpAsync(string modelId)
    {
        await _lock.WaitAsync();
        try { await Task.Run(() => EnsureFactory(modelId)); }
        finally { _lock.Release(); }
    }

    public async Task<string> TranscribeAsync(float[] samples, AppSettings settings, CancellationToken ct, string? context = null)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var factory = await Task.Run(() => EnsureFactory(settings.WhisperModel), ct);

            // Whisper braucht mindestens ~1 s Audio; kurze Aufnahmen mit Stille auffüllen.
            if (samples.Length < AudioRecorder.SampleRate * 1.2)
                Array.Resize(ref samples, (int)(AudioRecorder.SampleRate * 1.2));

            var builder = factory.CreateBuilder()
                .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 8));
            builder = settings.Language == "auto"
                ? builder.WithLanguageDetection()
                : builder.WithLanguage(settings.Language);
            // Prompt: Fachbegriffe + Ende des vorigen Abschnitts (hilft bei Schreibweise und Satzanschluss).
            var prompt = $"{settings.Vocabulary?.Trim()} {context?.Trim()}".Trim();
            if (prompt.Length > 0)
                builder = builder.WithPrompt(prompt);

            var text = new StringBuilder();
            await using var processor = builder.Build();
            await foreach (var segment in processor.ProcessAsync(samples, ct))
                text.Append(segment.Text);

            return Clean(text.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    private WhisperFactory EnsureFactory(string modelId)
    {
        var path = ModelPath(modelId);
        if (_factory is not null && _loadedPath == path) return _factory;
        if (!File.Exists(path)) throw new FileNotFoundException("Das Spracherkennungs-Modell ist noch nicht heruntergeladen.", path);
        _factory?.Dispose();
        _factory = WhisperFactory.FromPath(path);
        _loadedPath = path;
        Log.Info($"Whisper-Modell geladen: {modelId}");
        return _factory;
    }

    private static string Clean(string text)
    {
        text = Hallucinations.Replace(text, " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
    }
}
