using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeechToText;

internal static class Program
{
    private enum State { Idle, Recording, Transcribing }

    private static State _state = State.Idle;
    private static AudioCapture? _capture;
    private static GroqClient _groq = null!;
    private static SynchronizationContext _ui = null!;

    [STAThread]
    private static int Main()
    {
        string? apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        // string? apiKey = "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";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                "GROQ_API_KEY environment variable is not set.\n\nSet it and restart.",
                "SpeechToText",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }

        _groq = new GroqClient(apiKey);

        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _ui = SynchronizationContext.Current!;

        using var hook = new KeyboardHook();
        hook.HotkeyPressed += OnHotkey;
        hook.Install();

        Application.Run();
        return 0;
    }

    private static void OnHotkey()
    {
        switch (_state)
        {
            case State.Idle:
                StartRecording();
                break;
            case State.Recording:
                StopAndTranscribe();
                break;
            case State.Transcribing:
                // tracer bullet: drop taps while in-flight (queueing comes in a later slice)
                break;
        }
    }

    private static void StartRecording()
    {
        try
        {
            _capture = new AudioCapture();
            _capture.Start();
            _state = State.Recording;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"start failed: {ex}");
            _capture?.Dispose();
            _capture = null;
            _state = State.Idle;
        }
    }

    private static void StopAndTranscribe()
    {
        if (_capture == null) { _state = State.Idle; return; }

        byte[] wav;
        try
        {
            wav = _capture.StopAndGetWav();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"stop failed: {ex}");
            _capture.Dispose();
            _capture = null;
            _state = State.Idle;
            return;
        }
        finally
        {
            _capture?.Dispose();
            _capture = null;
        }

        IntPtr targetHwnd = GetForegroundWindow();
        _state = State.Transcribing;

        _ = Task.Run(async () =>
        {
            string? text = null;
            try
            {
                text = await _groq.TranscribeAsync(wav).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"transcribe failed: {ex}");
            }

            _ui.Post(_ =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(text))
                        Paster.Paste(text!, targetHwnd);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"paste failed: {ex}");
                }
                finally
                {
                    _state = State.Idle;
                }
            }, null);
        });
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
