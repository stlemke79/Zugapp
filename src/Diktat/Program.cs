using System;
using System.Threading;
using System.Windows.Forms;
using Velopack;

namespace Diktat;

internal static class Program
{
    /// <summary>Gesetzt, wenn Diktat gerade nach einem Update neu gestartet wurde.</summary>
    public static string? UpdatedToVersion { get; private set; }

    [STAThread]
    private static void Main()
    {
        // Muss als Allererstes laufen: erledigt Installation/Update/Deinstallation und spielt fertige Updates ein.
        VelopackApp.Build()
            .OnRestarted(v => UpdatedToVersion = v.ToString())
            .Run();

        using var mutex = new Mutex(true, @"Local\Diktat.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Diktat läuft bereits (Symbol unten rechts im Infobereich).",
                "Diktat", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Unbehandelter Fehler", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unbehandelter Fehler", e.ExceptionObject as Exception);

        Application.Run(new TrayApp());
    }
}
