using System;
using System.Threading;
using System.Windows.Forms;

namespace Zugapp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\Zugapp.Diktat.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Zugapp Diktat läuft bereits (Symbol unten rechts im Infobereich).",
                "Zugapp Diktat", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Unbehandelter Fehler", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unbehandelter Fehler", e.ExceptionObject as Exception);

        Application.Run(new TrayApp());
    }
}
