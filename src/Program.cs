using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeechToText;

internal static class Program
{
    private static ConsoleCtrlDelegate? _ctrlHandler;

    [STAThread]
    private static int Main()
    {
        // string? apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        string? apiKey = "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";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                "GROQ_API_KEY environment variable is not set.\n\nSet it and restart.",
                "SpeechToText",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }

        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var ui = SynchronizationContext.Current!;

        using var hook = new KeyboardHook();
        var orchestrator = new DictationOrchestrator(
            hotkey: hook,
            audio: new AudioCapturerAdapter(),
            backend: new GroqClient(apiKey),
            targeter: new WindowTargeterAdapter(),
            paster: new ClipboardPasterAdapter(ui),
            clock: new SystemClock());

        orchestrator.ErrorFlashRequested += () =>
            Console.Error.WriteLine("error-flash: dictation dropped (empty or failed)");

        hook.Install();
        InstallCtrlCHandler(ui);

        Application.Run();
        orchestrator.Dispose();
        return 0;
    }

    private static void InstallCtrlCHandler(SynchronizationContext ui)
    {
        // Debug aid: when launched from a terminal (`dotnet run`), let Ctrl+C exit the
        // WinForms message loop. No-op when no console is attached.
        _ctrlHandler = _ =>
        {
            ui.Post(_ => Application.Exit(), null);
            return true;
        };
        SetConsoleCtrlHandler(_ctrlHandler, true);
    }

    private delegate bool ConsoleCtrlDelegate(int ctrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, [MarshalAs(UnmanagedType.Bool)] bool add);
}
