using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Diktat;

internal enum UpdateCheckResult { NotInstalled, UpToDate, Downloaded, Failed }

/// <summary>
/// Auto-Update über Velopack: neue Versionen kommen als GitHub-Release (siehe .github/workflows/windows-build.yml).
/// Funktioniert nur in der installierten Version (DiktatApp-win-Setup.exe), nicht in einer entpackten ZIP.
/// </summary>
internal sealed class Updater
{
    private const string RepoUrl = "https://github.com/stlemke79/Zugapp";

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, false, null));
    private readonly SemaphoreSlim _busy = new(1, 1);

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? "Entwicklerversion";

    public VelopackAsset? Pending => _manager.UpdatePendingRestart;

    public string? LastError { get; private set; }

    public async Task<UpdateCheckResult> CheckAndDownloadAsync(CancellationToken ct = default)
    {
        if (!IsInstalled) return UpdateCheckResult.NotInstalled;
        if (!await _busy.WaitAsync(0, ct)) return UpdateCheckResult.UpToDate;
        try
        {
            if (Pending is not null) return UpdateCheckResult.Downloaded;
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null) return UpdateCheckResult.UpToDate;

            Log.Info($"Update gefunden: {info.TargetFullRelease.Version} – wird heruntergeladen");
            await _manager.DownloadUpdatesAsync(info, null, ct);
            Log.Info($"Update {info.TargetFullRelease.Version} heruntergeladen");
            LastError = null;
            return UpdateCheckResult.Downloaded;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Error("Update-Prüfung fehlgeschlagen", ex);
            return UpdateCheckResult.Failed;
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>Beendet Diktat, installiert das heruntergeladene Update und startet neu.</summary>
    public void ApplyAndRestart()
    {
        if (Pending is { } asset)
        {
            Log.Info($"Installiere Update {asset.Version} und starte neu");
            _manager.ApplyUpdatesAndRestart(asset);
        }
    }
}
