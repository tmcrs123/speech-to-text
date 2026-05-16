using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeechToText;

// Notification-area icon driven by DictationOrchestrator events.
// Owns its NotifyIcon, the icon images per phase, the error-flash timer, and
// the right-click context menu. All NotifyIcon mutation happens on the UI
// thread via the SynchronizationContext supplied at construction time.
internal sealed class TrayIcon : IDisposable
{
    private const int ErrorFlashMs = 2000;

    private readonly SynchronizationContext _ui;
    private readonly ISoundPlayer _sounds;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _errorFlashTimer;
    private readonly ToolStripMenuItem _muteItem;

    private readonly IconHandle _idleIcon;
    private readonly IconHandle _recordingIcon;
    private readonly IconHandle _transcribingIcon;
    private readonly IconHandle _pastingIcon;
    private readonly IconHandle _errorIcon;

    private DictationState _state = DictationState.Idle;
    private bool _flashing;
    private bool _disposed;

    public event Action? QuitRequested;
    public event Action? SettingsRequested;

    public TrayIcon(SynchronizationContext ui, ISoundPlayer sounds)
    {
        _ui = ui;
        _sounds = sounds;

        _idleIcon = IconFactory.MakeMicIcon(Color.FromArgb(180, 180, 180), filled: false);
        _recordingIcon = IconFactory.MakeMicIcon(Color.FromArgb(220, 40, 40), filled: true);
        _transcribingIcon = IconFactory.MakeMicIcon(Color.FromArgb(60, 130, 220), filled: true);
        _pastingIcon = _transcribingIcon; // Acceptable per AC: reuse Transcribing icon.
        _errorIcon = IconFactory.MakeFlashIcon(Color.FromArgb(255, 30, 30));

        var menu = new ContextMenuStrip();
        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        _muteItem = new ToolStripMenuItem("Mute sounds") { CheckOnClick = true, Checked = sounds.Muted };
        _muteItem.CheckedChanged += (_, _) => _sounds.Muted = _muteItem.Checked;
        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => QuitRequested?.Invoke();
        menu.Items.Add(settingsItem);
        menu.Items.Add(_muteItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);
        menu.Opening += (_, _) => _muteItem.Checked = _sounds.Muted;

        _notifyIcon = new NotifyIcon
        {
            Icon = _idleIcon.Icon,
            Text = "SpeechToText — Idle",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _errorFlashTimer = new System.Windows.Forms.Timer { Interval = ErrorFlashMs };
        _errorFlashTimer.Tick += (_, _) => EndErrorFlash();
    }

    // Subscribe to orchestrator. Called once after construction; the orchestrator
    // fires StateChanged from worker threads, so we marshal to the UI thread.
    public void Attach(DictationOrchestrator orchestrator)
    {
        orchestrator.StateChanged += OnStateChanged;
        orchestrator.ErrorFlashRequested += OnErrorFlash;
    }

    private void OnStateChanged(DictationState next)
    {
        _ui.Post(_ => ApplyState(next), null);
    }

    private void OnErrorFlash()
    {
        _ui.Post(_ => BeginErrorFlash(), null);
    }

    private void ApplyState(DictationState next)
    {
        if (_disposed) return;

        var prev = _state;
        _state = next;

        // Audio cues key off the visible state edge — ping on entering Recording,
        // pong on leaving it (any cause: tap, max-duration, Esc).
        if (prev != DictationState.Recording && next == DictationState.Recording)
            _sounds.PlayStartPing();
        else if (prev == DictationState.Recording && next != DictationState.Recording)
            _sounds.PlayStopPong();

        if (_flashing) return; // Don't clobber the flash icon mid-flash.
        UpdateIcon();
    }

    private void BeginErrorFlash()
    {
        if (_disposed) return;
        _flashing = true;
        _notifyIcon.Icon = _errorIcon.Icon;
        _notifyIcon.Text = "SpeechToText — Error";
        _errorFlashTimer.Stop();
        _errorFlashTimer.Start();
    }

    private void EndErrorFlash()
    {
        _errorFlashTimer.Stop();
        _flashing = false;
        UpdateIcon();
    }

    private void UpdateIcon()
    {
        _notifyIcon.Icon = _state switch
        {
            DictationState.Recording => _recordingIcon.Icon,
            DictationState.Transcribing => _transcribingIcon.Icon,
            DictationState.Pasting => _pastingIcon.Icon,
            _ => _idleIcon.Icon,
        };
        _notifyIcon.Text = $"SpeechToText — {_state}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _errorFlashTimer.Stop();
        _errorFlashTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _idleIcon.Dispose();
        _recordingIcon.Dispose();
        _transcribingIcon.Dispose();
        // _pastingIcon aliases _transcribingIcon — don't double-free.
        _errorIcon.Dispose();
    }

    // Pairs a managed Icon with the HICON it wraps so Dispose can release both.
    // GetHicon returns a handle we must free with DestroyIcon; Icon.FromHandle
    // creates a non-owning wrapper, so without this we'd leak the HICON.
    private sealed class IconHandle : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public Icon Icon { get; }
        private IntPtr _hIcon;

        public IconHandle(IntPtr hIcon)
        {
            _hIcon = hIcon;
            Icon = Icon.FromHandle(hIcon);
        }

        public void Dispose()
        {
            Icon.Dispose();
            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }
    }

    private static class IconFactory
    {
        public static IconHandle MakeMicIcon(Color color, bool filled)
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                var capsuleRect = new Rectangle(11, 4, 10, 16);
                using var pen = new Pen(color, 2.5f);
                using var brush = new SolidBrush(color);
                if (filled) g.FillEllipse(brush, capsuleRect);
                else g.DrawEllipse(pen, capsuleRect);

                g.DrawArc(pen, new Rectangle(7, 13, 18, 14), 20f, 140f);
                g.DrawLine(pen, 16, 23, 16, 28);
                g.DrawLine(pen, 11, 28, 21, 28);
            }
            return new IconHandle(bmp.GetHicon());
        }

        public static IconHandle MakeFlashIcon(Color color)
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, 4, 4, 24, 24);
                using var crossPen = new Pen(Color.White, 3f);
                g.DrawLine(crossPen, 11, 11, 21, 21);
                g.DrawLine(crossPen, 21, 11, 11, 21);
            }
            return new IconHandle(bmp.GetHicon());
        }
    }
}
