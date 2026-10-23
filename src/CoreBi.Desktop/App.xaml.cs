using System.Threading;
using System.Runtime.InteropServices;

using System.Windows;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Desktop;

public partial class App : Application
{
    // Uma única instância: abrir o CoreBi de novo só traz a janela que já está aberta para a frente.
    private static Mutex? _single;
    private static bool _ownsMutex;

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            DiagnosticLog.Write("ui.unhandled", e.Exception);
            MessageBox.Show("O CoreBi encontrou uma falha inesperada e será encerrado. Consulte o registro local em " + DiagnosticLog.Folder,
                "CoreBi", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown(1);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { DiagnosticLog.Write("task.unobserved", e.Exception); e.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception error) DiagnosticLog.Write("process.unhandled", error); };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    protected override void OnStartup(StartupEventArgs e)
    {
        bool first;
        try { _single = new Mutex(true, @"Local\CoreBi.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; } // criado por outra instância com outro nível de permissão

        if (!first)
        {
            IntPtr h = FindWindow(null, "CoreBi");
            if (h != IntPtr.Zero)
            {
                if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                SetForegroundWindow(h);
            }
            Shutdown();
            return;
        }
        _ownsMutex = true;
        base.OnStartup(e);
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _single?.ReleaseMutex();
        _single?.Dispose();
        base.OnExit(e);
    }
}
