using System.Diagnostics;
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
    private static IDisposable? _backendDisposable;

    [STAThread]
    private static int Main(string[] args)
    {
        // --minimized is set by the HKCU Run entry when the app is launched on
        // login. The startup path already starts to-tray with no visible
        // window, so the flag is a marker for future-proofing and tracing.
        bool launchedMinimized = args.Any(a =>
            string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (launchedMinimized) Trace.TraceInformation("Program: launched with --minimized.");

        var configStore = ConfigStore.Default();
        var autoStart = LoginAutoStart.Default();

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
        var backend = ResolveBackend(configStore, out var backendError);
        _backendDisposable = backend as IDisposable;
        var audioCapturer = new AudioCapturerAdapter(configStore);
        var orchestrator = new DictationOrchestrator(
            hotkey: hook,
            audio: audioCapturer,
            backend: backend,
            targeter: new WindowTargeterAdapter(),
            paster: new ClipboardPasterAdapter(ui),
            clock: new SystemClock(),
            maxDurationProvider: () => TimeSpan.FromSeconds(Math.Max(1, configStore.GetMaxRecordingSeconds())));

        using var sounds = new NAudioSoundPlayer();
        sounds.Muted = !configStore.GetStartStopSoundsEnabled();
        using var tray = new TrayIcon(ui, sounds);
        tray.Attach(orchestrator);
        tray.QuitRequested += () => ui.Post(_ => Application.Exit(), null);
        tray.SettingsRequested += () => ui.Post(_ => OpenSettings(configStore, hook, sounds, autoStart), null);

        // Recording Indicator: passive, click-through, always-on-top WPF window
        // driven by RecordingActiveChanged. Created once at startup so first-show
        // latency doesn't reach the user (issue #25 AC).
        var indicator = new RecordingIndicatorWindow();
        indicator.Attach(orchestrator, audioCapturer, configStore);

        hook.Install();
        InstallCtrlCHandler(ui);

        // First-run: walk the user through backend / hotkey / audio setup.
        // The wizard re-opens on every launch until WizardCompleted is true,
        // so closing it mid-flow leaves the app unconfigured (and re-prompts).
        // Also re-open if the configured Local Backend couldn't initialize
        // (e.g. CUDA missing after a driver change, or the model file was
        // deleted) — the user needs a chance to pick a different runtime or
        // switch to Cloud.
        if (!configStore.GetWizardCompleted() || backendError != null)
        {
            if (backendError != null)
            {
                Trace.TraceError($"Local Backend init failed: {backendError.GetType().Name}: {backendError.Message}");
                MessageBox.Show(
                    $"The Local Backend could not start:\n\n{backendError.Message}\n\nPlease re-run the setup wizard to download the model or switch to the Cloud backend.",
                    "SpeechToText — Local Backend unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                configStore.SetWizardCompleted(false);
            }
            ui.Post(_ => OpenWizard(configStore, hook, autoStart), null);
        }

        Application.Run();
        orchestrator.Dispose();
        indicator.Close();
        _backendDisposable?.Dispose();
        return 0;
    }

    private static ITranscriptionBackend ResolveBackend(ConfigStore config, out Exception? error)
    {
        error = null;
        var name = config.GetTranscriptionBackend();
        if (string.Equals(name, "local", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var modelPath = WhisperModelDownloader.ModelPath(config.GetLocalModel());
                return new LocalBackend(modelPath);
            }
            catch (Exception ex)
            {
                error = ex;
                // Fall through to a no-op backend so the message loop can still
                // run and surface the wizard prompt. The user will pick a real
                // backend in the wizard and restart per ADR-0001.
                return new UnavailableBackend(ex.Message);
            }
        }
        return new GroqClient(() => config.GetGroqApiKey());
    }

    private sealed class UnavailableBackend : ITranscriptionBackend
    {
        private readonly string _reason;
        public UnavailableBackend(string reason) => _reason = reason;
        public Task<string> TranscribeAsync(byte[] wav, CancellationToken ct) =>
            Task.FromException<string>(new InvalidOperationException(
                $"Transcription Backend is unavailable: {_reason}. Re-run the setup wizard."));
    }

    private static void OpenSettings(ConfigStore config, KeyboardHook hook, ISoundPlayer sounds, LoginAutoStart autoStart)
    {
        if (_openSettings != null)
        {
            _openSettings.Activate();
            return;
        }
        _openSettings = new SettingsWindow(config, hook, sounds, autoStart);
        _openSettings.Closed += (_, _) => _openSettings = null;
        _openSettings.Show();
    }

    private static void OpenWizard(ConfigStore config, KeyboardHook hook, LoginAutoStart autoStart)
    {
        if (_openWizard != null)
        {
            _openWizard.Activate();
            return;
        }
        _openWizard = new WizardWindow(config, hook, autoStart: autoStart);
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
