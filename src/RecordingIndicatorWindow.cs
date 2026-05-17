using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Rectangle = System.Windows.Shapes.Rectangle;
using VerticalAlignment = System.Windows.VerticalAlignment;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace SpeechToText;

// Passive, click-through, always-on-top window that signals an active
// Recording (see ADR-0005). Created once at startup (hidden) and shown/hidden
// on DictationOrchestrator.RecordingActiveChanged. Never takes focus and
// never receives input — preserving WindowTargeter.CaptureHwndNow() at
// Recording-end and click-through to the window beneath it is load-bearing.
internal sealed class RecordingIndicatorWindow : Window
{
    private const double WindowWidthDip = 120;
    private const double WindowHeightDip = 36;
    private const double BottomMarginDip = 80;

    // Bar heights (DIPs) at full-scale signal. Center bar is tallest.
    private static readonly double[] MaxHeights = { 12, 16, 20, 16, 12 };
    private const double MinBarHeight = 2.0;

    private Rectangle[] _bars = Array.Empty<Rectangle>();
    private volatile float _latestLevel;
    private float _envelope;
    private double _idlePhase;
    private DispatcherTimer? _meterTimer;
    private ConfigStore? _config;

    public RecordingIndicatorWindow()
    {
        Title = "SpeechToText Recording Indicator";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = WindowWidthDip;
        Height = WindowHeightDip;
        Focusable = false;
        // Park off-screen until first show so the initial creation has no
        // visible flash if Show()/Hide() is ever swapped in by future code.
        Left = -32000;
        Top = -32000;
        Content = BuildGlyph();
        // Force HWND creation now so the extended styles are stamped before
        // any Show() — first-show latency is otherwise visible (issue #25 AC).
        new WindowInteropHelper(this).EnsureHandle();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        // TRANSPARENT + LAYERED: mouse events pass through to the window beneath.
        // NOACTIVATE: the window never steals focus from the target app.
        // TOOLWINDOW: keeps the indicator out of Alt-Tab.
        SetWindowLong(hwnd, GWL_EXSTYLE,
            ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    // Subscribe to orchestrator and capturer. Called once after construction;
    // RecordingActiveChanged fires from worker threads so we marshal to the
    // WPF dispatcher. LevelChanged fires from the audio capture thread at ~20 Hz.
    public void Attach(DictationOrchestrator orchestrator, IAudioCapturer capturer, ConfigStore config)
    {
        _config = config;
        capturer.LevelChanged += OnLevelChanged;
        orchestrator.RecordingActiveChanged += OnRecordingActiveChanged;
    }

    // Written from the audio capture thread; read on the UI thread via the 30 Hz timer.
    private void OnLevelChanged(float level) => _latestLevel = level;

    private void OnRecordingActiveChanged(bool active)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyActive(active);
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyActive(active)));
        }
    }

    private void ApplyActive(bool active)
    {
        if (active)
        {
            if (_config?.GetShowRecordingIndicator() == false) return;
            PositionAtFocusedMonitor();
            if (!IsVisible) Show();
            StartMeterTimer();
        }
        else
        {
            StopMeterTimer();
            ResetBars();
            if (IsVisible) Hide();
        }
    }

    private void StartMeterTimer()
    {
        if (_meterTimer != null) return;
        _meterTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30 Hz
        };
        _meterTimer.Tick += OnMeterTick;
        _meterTimer.Start();
    }

    private void StopMeterTimer()
    {
        if (_meterTimer == null) return;
        _meterTimer.Stop();
        _meterTimer.Tick -= OnMeterTick;
        _meterTimer = null;
    }

    private void OnMeterTick(object? sender, EventArgs e)
    {
        float raw = _latestLevel;

        // Envelope follower: fast attack so speech onset is perceptible within ~50 ms,
        // faster release so muted-mic silence is visible within ~100 ms.
        const float AttackFactor = 0.80f;
        const float ReleaseFactor = 0.50f;
        float factor = raw > _envelope ? AttackFactor : ReleaseFactor;
        _envelope = Math.Clamp(_envelope + (raw - _envelope) * factor, 0f, 1f);

        // Log scale so normal conversational speech (RMS ~0.01–0.1, i.e. -40 to -20 dBFS)
        // maps to the middle of the bar range rather than staying near the floor.
        float display = RmsToDisplay(_envelope);

        // Idle animation: gentle sine-wave breathing when signal is truly silent,
        // so the indicator is visibly alive even when the mic picks up nothing.
        // Fades out as real signal arrives (display > 0.15 ≈ RMS ~0.007).
        _idlePhase += 0.10; // ~0.5 Hz at 30 Hz timer
        float idleAmplitude = 0.20f * Math.Max(0f, 1f - display / 0.15f);
        double breathe = idleAmplitude * (Math.Sin(_idlePhase) + 1.0) / 2.0;
        display = Math.Max(display, (float)breathe);

        UpdateBars(display);
    }

    // Maps linear RMS [0, 1] to a display value [0, 1].
    // 30 dB window: floor at −48 dBFS (silence), full scale at −18 dBFS.
    // Conversational speech (RMS ~0.05–0.15, i.e. −26 to −16 dBFS) maps to
    // 70–100% of the bar range so movement is clearly visible.
    private static float RmsToDisplay(float rms)
    {
        if (rms <= 0f) return 0f;
        float db = 20f * (float)Math.Log10(rms);
        return Math.Clamp((db + 48f) / 10f, 0f, 1f);
    }

    private void UpdateBars(float level)
    {
        for (int i = 0; i < _bars.Length; i++)
            _bars[i].Height = MinBarHeight + (MaxHeights[i] - MinBarHeight) * level;
    }

    private void ResetBars()
    {
        _latestLevel = 0;
        _envelope = 0;
        foreach (var bar in _bars)
            bar.Height = MinBarHeight;
    }

    private void PositionAtFocusedMonitor()
    {
        IntPtr fg = GetForegroundWindow();
        var screen = fg != IntPtr.Zero
            ? WinFormsScreen.FromHandle(fg)
            : WinFormsScreen.PrimaryScreen;
        if (screen == null) return;

        // System.Windows.Forms.Screen.WorkingArea is in physical pixels.
        // Convert to DIPs so WPF's Left/Top (which are DIPs) land in the
        // right place — important on per-monitor-DPI systems.
        var source = PresentationSource.FromVisual(this)
            ?? HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var topLeft = fromDevice.Transform(new System.Windows.Point(
            screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(
            screen.WorkingArea.Right, screen.WorkingArea.Bottom));

        Left = (topLeft.X + bottomRight.X - WindowWidthDip) / 2;
        Top = bottomRight.Y - WindowHeightDip - BottomMarginDip;
    }

    private UIElement BuildGlyph()
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(180, 24, 24, 24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(140, 220, 60, 60)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
        };
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var fill = new SolidColorBrush(Color.FromArgb(220, 230, 230, 230));
        _bars = new Rectangle[MaxHeights.Length];
        for (int i = 0; i < MaxHeights.Length; i++)
        {
            var rect = new Rectangle
            {
                Width = 3,
                Height = MinBarHeight,
                Margin = new Thickness(2, 0, 2, 0),
                Fill = fill,
                RadiusX = 1.5,
                RadiusY = 1.5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _bars[i] = rect;
            panel.Children.Add(rect);
        }
        border.Child = panel;
        return border;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
