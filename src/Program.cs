using System.Runtime.InteropServices;
using System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace SpeechToText;

internal static class Program
{
    private static ConsoleCtrlDelegate? _ctrlHandler;
    private static SettingsWindow? _openSettings;
    private static WizardWindow? _openWizard;
    private static WpfApplication? _wpfApp;

    [STAThread]
    private static int Main()
    {
        var configStore = ConfigStore.Default();

        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var ui = SynchronizationContext.Current!;

        // Bootstrap the WPF Application object so we can host WPF windows from
        // a WinForms message loop. Without this, Application.Current is null
        // inside the settings window's resource lookup.
        _wpfApp = new WpfApplication { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

        var initialChord = ChordDescriptor.TryParse(configStore.GetHotkey(), out var c)
            ? c
            : new ChordDescriptor(ChordModifiers.Ctrl | ChordModifiers.Shift, System.Windows.Forms.Keys.Space);

        using var hook = new KeyboardHook(initialChord);
        var orchestrator = new DictationOrchestrator(
            hotkey: hook,
            audio: new AudioCapturerAdapter(configStore),
            backend: new GroqClient(() => configStore.GetGroqApiKey()),
            targeter: new WindowTargeterAdapter(),
            paster: new ClipboardPasterAdapter(ui),
            clock: new SystemClock(),
            maxDurationProvider: () => TimeSpan.FromSeconds(Math.Max(1, configStore.GetMaxRecordingSeconds())));

        using var sounds = new NAudioSoundPlayer();
        sounds.Muted = !configStore.GetStartStopSoundsEnabled();
        using var tray = new TrayIcon(ui, sounds);
        tray.Attach(orchestrator);
        tray.QuitRequested += () => ui.Post(_ => Application.Exit(), null);
        tray.SettingsRequested += () => ui.Post(_ => OpenSettings(configStore, hook, sounds), null);

        hook.Install();
        InstallCtrlCHandler(ui);

        // First-run: walk the user through backend / hotkey / audio setup.
        // The wizard re-opens on every launch until WizardCompleted is true,
        // so closing it mid-flow leaves the app unconfigured (and re-prompts).
        if (!configStore.GetWizardCompleted())
        {
            ui.Post(_ => OpenWizard(configStore, hook), null);
        }

        Application.Run();
        orchestrator.Dispose();
        return 0;
    }

    private static void OpenSettings(ConfigStore config, KeyboardHook hook, ISoundPlayer sounds)
    {
        if (_openSettings != null)
        {
            _openSettings.Activate();
            return;
        }
        _openSettings = new SettingsWindow(config, hook, sounds);
        _openSettings.Closed += (_, _) => _openSettings = null;
        _openSettings.Show();
    }

    private static void OpenWizard(ConfigStore config, KeyboardHook hook)
    {
        if (_openWizard != null)
        {
            _openWizard.Activate();
            return;
        }
        _openWizard = new WizardWindow(config, hook);
        _openWizard.Closed += (_, _) => _openWizard = null;
        _openWizard.Show();
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
